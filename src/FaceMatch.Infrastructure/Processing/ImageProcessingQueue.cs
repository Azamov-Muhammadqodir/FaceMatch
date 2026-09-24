using System.Threading.Channels;
using FaceMatch.Core.Abstractions;

namespace FaceMatch.Infrastructure.Processing;

/// <summary>
/// Unbounded in-memory queue. It is only a fast path: the source of truth is the <c>status</c> column, and pending
/// images are re-enqueued from the database when the service starts.
/// </summary>
public sealed class ImageProcessingQueue : IImageProcessingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(Guid imageId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(imageId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
