using System.Text.Json.Serialization;

namespace FaceMatch.Core.Abstractions;

/// <summary>Axis-aligned bounding box in pixel coordinates.</summary>
public readonly record struct FaceBox(float X, float Y, float Width, float Height)
{
    [JsonIgnore]
    public float Area => Width * Height;
}

/// <summary>A face found by the analyzer with its identity embedding.</summary>
public sealed record AnalyzedFace(FaceBox Box, float Confidence, float[] Embedding, byte[]? ThumbnailJpeg);

/// <summary>Result of analysing a whole image.</summary>
public sealed record ImageAnalysis(int Width, int Height, IReadOnlyList<AnalyzedFace> Faces);

/// <summary>Detects faces in an image and produces identity embeddings for each of them.</summary>
public interface IFaceAnalyzer
{
    /// <summary>Embedding dimensionality produced by the recognition model.</summary>
    int EmbeddingSize { get; }

    /// <summary>
    /// Detects every face in <paramref name="image"/> and generates an embedding per face.
    /// </summary>
    /// <param name="image">Encoded image (JPEG, PNG, WebP, ...).</param>
    /// <param name="includeThumbnails">Whether to render a JPEG crop for each face.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidImageException">The content is not a decodable image.</exception>
    ImageAnalysis Analyze(Stream image, bool includeThumbnails, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when an uploaded payload cannot be decoded as an image.</summary>
public sealed class InvalidImageException(string message, Exception? inner = null) : Exception(message, inner);
