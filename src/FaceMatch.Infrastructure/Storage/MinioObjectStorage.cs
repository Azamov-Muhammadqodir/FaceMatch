using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace FaceMatch.Infrastructure.Storage;

public sealed class MinioObjectStorage(
    IMinioClient client,
    IOptions<StorageOptions> options,
    ILogger<MinioObjectStorage> logger) : IObjectStorage
{
    private readonly string _bucket = options.Value.Bucket;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), cancellationToken);
        if (!exists)
        {
            logger.LogInformation("Creating MinIO bucket {Bucket}", _bucket);
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), cancellationToken);
        }
    }

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken = default)
    {
        await client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(key)
                .WithStreamData(content)
                .WithObjectSize(length)
                .WithContentType(contentType),
            cancellationToken);
    }

    public async Task ReadAsync(string key, Stream destination, CancellationToken cancellationToken = default)
    {
        await client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(key)
                .WithCallbackStream((source, ct) => source.CopyToAsync(destination, ct)),
            cancellationToken);
    }

    public async Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (list.Count == 0)
        {
            return;
        }

        var errors = await client.RemoveObjectsAsync(
            new RemoveObjectsArgs().WithBucket(_bucket).WithObjects(list),
            cancellationToken);

        foreach (var error in errors)
        {
            logger.LogWarning("Failed to delete object {Key}: {Message}", error.Key, error.Message);
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MinIO health check failed");
            return false;
        }
    }
}
