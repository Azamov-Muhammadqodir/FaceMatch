using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Contracts;
using FaceMatch.Core.Domain;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FaceMatch.Infrastructure.Services;

/// <summary>Read and delete operations over indexed images.</summary>
public sealed class ImageCatalogService(FaceMatchDbContext db, IObjectStorage storage)
{
    public async Task<PagedResult<ImageSummaryDto>> ListAsync(int page, int pageSize, ImageStatus? status, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Images.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ImageSummaryDto>(items.Select(ToSummary).ToList(), page, pageSize, total);
    }

    public async Task<ImageDetailsDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (image is null)
        {
            return null;
        }

        var faces = await db.Faces.AsNoTracking()
            .Where(f => f.ImageId == id)
            .OrderBy(f => f.X)
            .Select(f => new { f.Id, f.X, f.Y, f.Width, f.Height, f.Confidence })
            .ToListAsync(cancellationToken);

        return new ImageDetailsDto(
            ToSummary(image),
            faces.Select(f => new FaceDto(f.Id, new FaceBox(f.X, f.Y, f.Width, f.Height), f.Confidence, ResourceUrls.FaceThumbnail(f.Id))).ToList());
    }

    public Task<ImageAsset?> FindImageAsync(Guid id, CancellationToken cancellationToken) =>
        db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<string?> FindThumbnailKeyAsync(Guid faceId, CancellationToken cancellationToken) =>
        db.Faces.AsNoTracking().Where(f => f.Id == faceId).Select(f => f.ThumbnailKey).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var image = await db.Images.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (image is null)
        {
            return false;
        }

        var keys = await db.Faces
            .Where(f => f.ImageId == id && f.ThumbnailKey != null)
            .Select(f => f.ThumbnailKey!)
            .ToListAsync(cancellationToken);
        keys.Add(image.ObjectKey);

        db.Images.Remove(image); // faces are removed by the cascading FK
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(keys, CancellationToken.None);
        return true;
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        var byStatus = await db.Images.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var faces = await db.Faces.LongCountAsync(cancellationToken);
        int Count(ImageStatus s) => byStatus.GetValueOrDefault(s);

        return new StatsDto(
            byStatus.Values.Sum(),
            Count(ImageStatus.Pending),
            Count(ImageStatus.Processing),
            Count(ImageStatus.Completed),
            Count(ImageStatus.Failed),
            faces);
    }

    private static ImageSummaryDto ToSummary(ImageAsset i) => new(
        i.Id,
        i.FileName,
        i.ContentType,
        i.SizeBytes,
        i.Width,
        i.Height,
        i.Status,
        i.FaceCount,
        i.Error,
        i.CreatedAt,
        i.ProcessedAt,
        ResourceUrls.ImageContent(i.Id));
}
