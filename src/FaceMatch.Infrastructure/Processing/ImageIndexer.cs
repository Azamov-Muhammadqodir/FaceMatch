using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace FaceMatch.Infrastructure.Processing;

public enum IndexingOutcome
{
    Skipped,
    Completed,
    Failed,
    RetryLater,
}

/// <summary>Detects and indexes the faces of a single stored image. Idempotent and safe to run concurrently.</summary>
public sealed class ImageIndexer(
    FaceMatchDbContext db,
    IObjectStorage storage,
    IFaceAnalyzer analyzer,
    TimeProvider clock,
    IOptions<ProcessingOptions> options,
    ILogger<ImageIndexer> logger)
{
    public async Task<IndexingOutcome> IndexAsync(Guid imageId, CancellationToken cancellationToken)
    {
        // Atomically claim the image so two workers (or two instances) never process it at the same time.
        var claimed = await db.Images
            .Where(i => i.Id == imageId && i.Status == ImageStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(i => i.Status, ImageStatus.Processing)
                      .SetProperty(i => i.Attempts, i => i.Attempts + 1),
                cancellationToken);

        if (claimed == 0)
        {
            return IndexingOutcome.Skipped;
        }

        var image = await db.Images.AsNoTracking().SingleAsync(i => i.Id == imageId, cancellationToken);
        var started = clock.GetTimestamp();
        var thumbnailKeys = new List<string>();

        try
        {
            using var buffer = new MemoryStream(capacity: (int)Math.Min(image.SizeBytes, int.MaxValue));
            await storage.ReadAsync(image.ObjectKey, buffer, cancellationToken);
            buffer.Position = 0;

            // CPU-bound inference; keeps the calling thread free when invoked from a request.
            var analysis = await Task.Run(() => analyzer.Analyze(buffer, includeThumbnails: true, cancellationToken), cancellationToken);

            var now = clock.GetUtcNow();
            var faces = new List<Face>(analysis.Faces.Count);
            foreach (var detected in analysis.Faces)
            {
                var face = new Face
                {
                    Id = Guid.CreateVersion7(),
                    ImageId = image.Id,
                    X = detected.Box.X,
                    Y = detected.Box.Y,
                    Width = detected.Box.Width,
                    Height = detected.Box.Height,
                    Confidence = detected.Confidence,
                    Embedding = new Vector(detected.Embedding),
                    CreatedAt = now,
                };

                if (detected.ThumbnailJpeg is { } jpeg)
                {
                    face.ThumbnailKey = $"faces/{image.Id:N}/{face.Id:N}.jpg";
                    using var thumbStream = new MemoryStream(jpeg, writable: false);
                    await storage.PutAsync(face.ThumbnailKey, thumbStream, jpeg.Length, "image/jpeg", cancellationToken);
                    thumbnailKeys.Add(face.ThumbnailKey);
                }

                faces.Add(face);
            }

            var staleThumbnails = await ReplaceFacesAsync(image.Id, faces, analysis, now, cancellationToken);
            await storage.DeleteAsync(staleThumbnails, CancellationToken.None);

            logger.LogInformation(
                "Indexed image {ImageId} ({FileName}): {FaceCount} face(s) in {Elapsed:N0} ms",
                image.Id,
                image.FileName,
                faces.Count,
                clock.GetElapsedTime(started).TotalMilliseconds);

            return IndexingOutcome.Completed;
        }
        catch (InvalidImageException ex)
        {
            logger.LogWarning("Image {ImageId} cannot be decoded: {Message}", image.Id, ex.Message);
            await MarkAsync(image.Id, ImageStatus.Failed, ex.Message, CancellationToken.None);
            await storage.DeleteAsync(thumbnailKeys, CancellationToken.None);
            return IndexingOutcome.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // `image` was loaded after the claim, so Attempts already includes this run.
            var final = image.Attempts >= options.Value.MaxAttempts;
            logger.LogError(ex, "Indexing image {ImageId} failed (attempt {Attempt})", image.Id, image.Attempts);
            await MarkAsync(image.Id, final ? ImageStatus.Failed : ImageStatus.Pending, Truncate(ex.Message), CancellationToken.None);
            await storage.DeleteAsync(thumbnailKeys, CancellationToken.None);
            return final ? IndexingOutcome.Failed : IndexingOutcome.RetryLater;
        }
        catch (OperationCanceledException)
        {
            // Shutting down: hand the image back so it is picked up on the next start.
            await MarkAsync(image.Id, ImageStatus.Pending, null, CancellationToken.None);
            await storage.DeleteAsync(thumbnailKeys, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Replaces all faces of the image in one transaction; returns thumbnail keys that are no longer referenced.</summary>
    private async Task<List<string>> ReplaceFacesAsync(
        Guid imageId,
        List<Face> faces,
        ImageAnalysis analysis,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var stale = await db.Faces
            .Where(f => f.ImageId == imageId && f.ThumbnailKey != null)
            .Select(f => f.ThumbnailKey!)
            .ToListAsync(cancellationToken);

        await db.Faces.Where(f => f.ImageId == imageId).ExecuteDeleteAsync(cancellationToken);

        db.Faces.AddRange(faces);
        await db.SaveChangesAsync(cancellationToken);

        await db.Images
            .Where(i => i.Id == imageId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(i => i.Status, ImageStatus.Completed)
                      .SetProperty(i => i.FaceCount, faces.Count)
                      .SetProperty(i => i.Width, analysis.Width)
                      .SetProperty(i => i.Height, analysis.Height)
                      .SetProperty(i => i.Error, (string?)null)
                      .SetProperty(i => i.ProcessedAt, now),
                cancellationToken);

        await tx.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return stale;
    }

    private Task<int> MarkAsync(Guid imageId, ImageStatus status, string? error, CancellationToken cancellationToken) =>
        db.Images
            .Where(i => i.Id == imageId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(i => i.Status, status)
                      .SetProperty(i => i.Error, error)
                      .SetProperty(i => i.ProcessedAt, clock.GetUtcNow()),
                cancellationToken);

    private static string Truncate(string message) => message.Length <= 2000 ? message : message[..2000];
}
