using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaceMatch.Infrastructure.Processing;

/// <summary>Background worker that indexes queued images with bounded parallelism.</summary>
public sealed class ImageProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IImageProcessingQueue queue,
    IOptions<ProcessingOptions> options,
    ILogger<ImageProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingAsync(stoppingToken);

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxDegreeOfParallelism,
            CancellationToken = stoppingToken,
        };

        logger.LogInformation("Image processing worker started with parallelism {Parallelism}", parallel.MaxDegreeOfParallelism);

        try
        {
            await Parallel.ForEachAsync(queue.DequeueAllAsync(stoppingToken), parallel, ProcessAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async ValueTask ProcessAsync(Guid imageId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var indexer = scope.ServiceProvider.GetRequiredService<ImageIndexer>();
            var outcome = await indexer.IndexAsync(imageId, cancellationToken);

            if (outcome == IndexingOutcome.RetryLater)
            {
                _ = RequeueLaterAsync(imageId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let one image take the worker down; the row stays Pending/Processing and is recovered on restart.
            logger.LogError(ex, "Unexpected error while processing image {ImageId}", imageId);
        }
    }

    private async Task RequeueLaterAsync(Guid imageId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RetryDelay, cancellationToken);
            await queue.EnqueueAsync(imageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; recovered on next start.
        }
    }

    /// <summary>Re-queues work that was interrupted by a restart (the queue itself is in-memory).</summary>
    private async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FaceMatchDbContext>();

        await db.Images
            .Where(i => i.Status == ImageStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, ImageStatus.Pending), cancellationToken);

        var pending = await db.Images
            .Where(i => i.Status == ImageStatus.Pending)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in pending)
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }

        if (pending.Count > 0)
        {
            logger.LogInformation("Re-queued {Count} pending image(s)", pending.Count);
        }
    }
}
