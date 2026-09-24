namespace FaceMatch.Core.Abstractions;

/// <summary>Binary object storage (backed by MinIO / S3).</summary>
public interface IObjectStorage
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Copies the object into <paramref name="destination"/>.</summary>
    Task ReadAsync(string key, Stream destination, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
