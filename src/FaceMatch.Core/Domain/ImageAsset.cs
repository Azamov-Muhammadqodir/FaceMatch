namespace FaceMatch.Core.Domain;

/// <summary>An uploaded image. The binary content lives in object storage (MinIO), metadata lives in PostgreSQL.</summary>
public class ImageAsset
{
    public Guid Id { get; set; }

    /// <summary>Original file name supplied by the client.</summary>
    public required string FileName { get; set; }

    /// <summary>Object key of the original image in the storage bucket.</summary>
    public required string ObjectKey { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Hex-encoded SHA-256 of the content; used to de-duplicate uploads.</summary>
    public required string Sha256 { get; set; }

    /// <summary>Width after EXIF auto-orientation (face boxes are relative to this).</summary>
    public int Width { get; set; }

    /// <summary>Height after EXIF auto-orientation (face boxes are relative to this).</summary>
    public int Height { get; set; }

    public ImageStatus Status { get; set; } = ImageStatus.Pending;

    public string? Error { get; set; }

    public int FaceCount { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public List<Face> Faces { get; set; } = [];
}
