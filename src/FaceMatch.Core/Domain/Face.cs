using Pgvector;

namespace FaceMatch.Core.Domain;

/// <summary>A single face detected inside an <see cref="ImageAsset"/>, together with its identity embedding.</summary>
public class Face
{
    public Guid Id { get; set; }

    public Guid ImageId { get; set; }

    public ImageAsset? Image { get; set; }

    /// <summary>Bounding box in pixels, relative to the auto-oriented image.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    /// <summary>Detector confidence in [0, 1].</summary>
    public float Confidence { get; set; }

    /// <summary>L2-normalised ArcFace embedding (512 dimensions).</summary>
    public required Vector Embedding { get; set; }

    /// <summary>Object key of the face thumbnail in the storage bucket.</summary>
    public string? ThumbnailKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
