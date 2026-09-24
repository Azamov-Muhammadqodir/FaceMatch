using System.Globalization;
using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Contracts;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FaceMatch.Infrastructure.Services;

/// <summary>Finds the faces most similar to a given face among every face of every indexed image.</summary>
public sealed class FaceSearchService(
    FaceMatchDbContext db,
    IFaceAnalyzer analyzer,
    IOptions<FaceRecognitionOptions> options,
    ILogger<FaceSearchService> logger)
{
    private const int MaxEfSearch = 1000;

    private readonly FaceRecognitionOptions _options = options.Value;

    /// <summary>Searches by the face in a query photo. If it contains several faces, the largest one is used unless <see cref="SearchOptions.FaceIndex"/> is set.</summary>
    /// <exception cref="InvalidImageException">The query is not a decodable image.</exception>
    /// <exception cref="ArgumentException">The requested face index does not exist.</exception>
    public async Task<SearchResultDto> SearchByImageAsync(Stream image, SearchOptions search, CancellationToken cancellationToken)
    {
        var analysis = await Task.Run(() => analyzer.Analyze(image, includeThumbnails: false, cancellationToken), cancellationToken);

        // Largest face first: that is almost always "the person" in a query photo.
        var ordered = analysis.Faces.OrderByDescending(f => f.Box.Area).ToList();
        var queryFaces = ordered.Select((f, i) => new QueryFaceDto(i, f.Box, f.Confidence)).ToList();
        var minSimilarity = ResolveMinSimilarity(search);

        if (ordered.Count == 0)
        {
            return new SearchResultDto(queryFaces, null, minSimilarity, 0, []);
        }

        var index = search.FaceIndex ?? 0;
        if (index < 0 || index >= ordered.Count)
        {
            throw new ArgumentException(
                $"faceIndex must be between 0 and {ordered.Count - 1}; the query image contains {ordered.Count} face(s).");
        }

        var matches = await FindNearestAsync(new Vector(ordered[index].Embedding), search.TopK, minSimilarity, excludeFaceId: null, cancellationToken);
        return new SearchResultDto(queryFaces, index, minSimilarity, matches.Select(m => m.ImageId).Distinct().Count(), matches);
    }

    /// <summary>Searches using an already indexed face ("find this person elsewhere").</summary>
    public async Task<SearchResultDto?> SearchByFaceAsync(Guid faceId, SearchOptions search, CancellationToken cancellationToken)
    {
        var embedding = await db.Faces.AsNoTracking()
            .Where(f => f.Id == faceId)
            .Select(f => f.Embedding)
            .SingleOrDefaultAsync(cancellationToken);

        if (embedding is null)
        {
            return null;
        }

        var minSimilarity = ResolveMinSimilarity(search);
        var matches = await FindNearestAsync(embedding, search.TopK, minSimilarity, faceId, cancellationToken);
        return new SearchResultDto([], null, minSimilarity, matches.Select(m => m.ImageId).Distinct().Count(), matches);
    }

    private float ResolveMinSimilarity(SearchOptions search) =>
        Math.Clamp(search.MinSimilarity ?? _options.DefaultMinSimilarity, -1f, 1f);

    private async Task<List<FaceMatchDto>> FindNearestAsync(
        Vector query,
        int topK,
        float minSimilarity,
        Guid? excludeFaceId,
        CancellationToken cancellationToken)
    {
        topK = Math.Clamp(topK, 1, _options.MaxSearchResults);
        var maxDistance = 1d - minSimilarity;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // HNSW returns at most ef_search candidates (pgvector caps it at 1000) before the WHERE filter is applied, so
        // widen it for large topK. Iterative scans (pgvector >= 0.8) keep walking the graph beyond that when needed.
        var efSearch = Math.Clamp(topK * 2, 100, MaxEfSearch).ToString(CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('hnsw.ef_search', {efSearch}, true)", cancellationToken);
        await TrySetIterativeScanAsync(cancellationToken);

        var rows = await db.Faces.AsNoTracking()
            .Where(f => excludeFaceId == null || f.Id != excludeFaceId)
            .Select(f => new
            {
                f.Id,
                f.ImageId,
                f.Image!.FileName,
                f.Image.Width,
                f.Image.Height,
                f.X,
                f.Y,
                FaceWidth = f.Width,
                FaceHeight = f.Height,
                Distance = f.Embedding.CosineDistance(query),
            })
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Take(topK)
            .ToListAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        logger.LogDebug("Face search returned {Count} match(es) (topK {TopK}, min similarity {Min})", rows.Count, topK, minSimilarity);

        return rows.Select(r => new FaceMatchDto(
                r.Id,
                r.ImageId,
                r.FileName,
                (float)Math.Round(1d - r.Distance, 4),
                new FaceBox(r.X, r.Y, r.FaceWidth, r.FaceHeight),
                r.Width,
                r.Height,
                ResourceUrls.ImageContent(r.ImageId),
                ResourceUrls.FaceThumbnail(r.Id)))
            .ToList();
    }

    private async Task TrySetIterativeScanAsync(CancellationToken cancellationToken)
    {
        // Older pgvector versions do not know this setting; a savepoint keeps the transaction usable if it fails.
        await db.Database.ExecuteSqlRawAsync("SAVEPOINT iterative_scan", cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = relaxed_order", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT iterative_scan", cancellationToken);
        }
        catch (Npgsql.PostgresException)
        {
            await db.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT iterative_scan", cancellationToken);
        }
    }
}
