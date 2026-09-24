namespace FaceMatch.Core.Abstractions;

/// <summary>In-process queue of image ids waiting to be indexed.</summary>
public interface IImageProcessingQueue
{
    ValueTask EnqueueAsync(Guid imageId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
