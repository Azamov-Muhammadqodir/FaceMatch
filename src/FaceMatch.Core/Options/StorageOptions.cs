using System.ComponentModel.DataAnnotations;

namespace FaceMatch.Core.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Minio";

    /// <summary>host:port of the MinIO server, e.g. <c>localhost:9000</c>.</summary>
    [Required]
    public string Endpoint { get; set; } = "localhost:9000";

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    public bool UseSsl { get; set; }

    [Required]
    public string Bucket { get; set; } = "facematch";
}
