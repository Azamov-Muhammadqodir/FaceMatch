using System.Security.Cryptography;
using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Contracts;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Persistence;
using FaceMatch.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SixLabors.ImageSharp;

namespace FaceMatch.Infrastructure.Services;

/// <summary>Validates uploads, stores the originals in MinIO and schedules them for face indexing.</summary>
public sealed class ImageIngestionService(
    FaceMatchDbContext db,
    IObjectStorage storage,
    IImageProcessingQueue queue,
    ImageIndexer indexer,
    TimeProvider clock,
    IOptions<ProcessingOptions> options,
    ILogger<ImageIngestionService> logger)
{
    /// <param name="files">Files to ingest.</param>
    /// <param name="waitForIndexing">
    /// When <c>true</c> faces are indexed before returning (handy for small uploads and scripts);
    /// otherwise indexing happens in the background and the caller polls the image status.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<UploadResult>> IngestAsync(
        IReadOnlyCollection<IncomingFile> files,
        bool waitForIndexing,
        CancellationToken cancellationToken)
    {
        var results = new List<UploadResult>(files.Count);
        foreach (var file in files)
        {
            var result = await IngestOneAsync(file, cancellationToken);

            if (result is { Outcome: UploadOutcome.Accepted, ImageId: { } id })
            {
                if (waitForIndexing)
                {
                    await indexer.IndexAsync(id, cancellationToken);
                    var state = await db.Images.AsNoTracking()
                        .Where(i => i.Id == id)
                        .Select(i => new { i.Status, i.FaceCount, i.Error })
                        .SingleAsync(cancellationToken);
                    result = result with { Status = state.Status, FaceCount = state.FaceCount, Error = state.Error };
                }
                else
                {
                    await queue.EnqueueAsync(id, cancellationToken);
                }
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>Puts an already stored image back into the indexing queue.</summary>
    public async Task<bool> ReindexAsync(Guid imageId, CancellationToken cancellationToken)
    {
        var updated = await db.Images
            .Where(i => i.Id == imageId && i.Status != ImageStatus.Processing)
            .ExecuteUpdateAsync(
                s => s.SetProperty(i => i.Status, ImageStatus.Pending)
                      .SetProperty(i => i.Attempts, 0)
                      .SetProperty(i => i.Error, (string?)null),
                cancellationToken);

        if (updated > 0)
        {
            await queue.EnqueueAsync(imageId, cancellationToken);
        }

        return updated > 0;
    }

    private async Task<UploadResult> IngestOneAsync(IncomingFile file, CancellationToken cancellationToken)
    {
        var fileName = SanitizeFileName(file.FileName);

        if (file.Length <= 0)
        {
            return Rejected(fileName, "The file is empty.");
        }

        if (file.Length > options.Value.MaxImageBytes)
        {
            return Rejected(fileName, $"The file exceeds the {options.Value.MaxImageBytes / (1024 * 1024)} MB limit.");
        }

        // Buffer once: we need the hash, the format sniffing and the upload from the same bytes.
        using var buffer = new MemoryStream((int)file.Length);
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(buffer, cancellationToken);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return Rejected(fileName, "The file is not a supported image (JPEG, PNG, WebP, BMP, GIF, TIFF).");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));

        var existing = await db.Images.AsNoTracking()
            .Where(i => i.Sha256 == sha256)
            .Select(i => new { i.Id, i.Status, i.FaceCount })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return new UploadResult(fileName, UploadOutcome.Duplicate, existing.Id, existing.Status, existing.FaceCount, null);
        }

        var format = info.Metadata.DecodedImageFormat;
        var id = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        var extension = format?.FileExtensions.FirstOrDefault() ?? "bin";
        var image = new ImageAsset
        {
            Id = id,
            FileName = fileName,
            ObjectKey = $"images/{now:yyyy/MM/dd}/{id:N}.{extension}",
            ContentType = format?.DefaultMimeType ?? "application/octet-stream",
            SizeBytes = buffer.Length,
            Sha256 = sha256,
            Width = info.Width,
            Height = info.Height,
            Status = ImageStatus.Pending,
            CreatedAt = now,
        };

        buffer.Position = 0;
        await storage.PutAsync(image.ObjectKey, buffer, buffer.Length, image.ContentType, cancellationToken);

        db.Images.Add(image);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request stored the same bytes first.
            db.ChangeTracker.Clear();
            await storage.DeleteAsync([image.ObjectKey], CancellationToken.None);
            var winner = await db.Images.AsNoTracking().SingleAsync(i => i.Sha256 == sha256, cancellationToken);
            return new UploadResult(fileName, UploadOutcome.Duplicate, winner.Id, winner.Status, winner.FaceCount, null);
        }

        logger.LogInformation("Stored image {ImageId} ({FileName}, {Size} bytes)", id, fileName, image.SizeBytes);
        return new UploadResult(fileName, UploadOutcome.Accepted, id, ImageStatus.Pending, null, null);
    }

    private static UploadResult Rejected(string fileName, string error) =>
        new(fileName, UploadOutcome.Rejected, null, null, null, error);

    private static string SanitizeFileName(string? name)
    {
        var fileName = Path.GetFileName(name ?? string.Empty).Trim();
        if (fileName.Length == 0)
        {
            return "unnamed";
        }

        return fileName.Length <= 255 ? fileName : fileName[^255..];
    }
}
