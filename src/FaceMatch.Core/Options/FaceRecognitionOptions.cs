using System.ComponentModel.DataAnnotations;

namespace FaceMatch.Core.Options;

public sealed class FaceRecognitionOptions
{
    public const string SectionName = "FaceRecognition";

    /// <summary>Minimum detector confidence for a face to be indexed.</summary>
    [Range(0.05, 1.0)]
    public float DetectionConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>Faces whose shorter side is smaller than this (in pixels) are ignored: their embeddings are unreliable.</summary>
    [Range(8, 1024)]
    public int MinFaceSize { get; set; } = 20;

    /// <summary>
    /// Longest side the detector works at. Larger values find smaller faces in crowd photos at the cost of speed.
    /// Must be a multiple of 32.
    /// </summary>
    [Range(320, 4096)]
    public int DetectorMaxInputSize { get; set; } = 1280;

    /// <summary>Default cosine similarity required for a search hit. ArcFace: ~0.35-0.45 is a sensible range.</summary>
    [Range(0.0, 1.0)]
    public float DefaultMinSimilarity { get; set; } = 0.4f;

    /// <summary>Upper bound for the number of hits a single search may return.</summary>
    [Range(1, 10_000)]
    public int MaxSearchResults { get; set; } = 1000;

    /// <summary>Edge length of the stored face thumbnails.</summary>
    [Range(32, 512)]
    public int ThumbnailSize { get; set; } = 160;

    /// <summary>Images with more pixels than this are rejected (protects against decompression bombs).</summary>
    [Range(1_000_000, 500_000_000)]
    public long MaxImagePixels { get; set; } = 80_000_000;

    /// <summary>Optional override for the SCRFD detector model path (defaults to the bundled model).</summary>
    public string? DetectorModelPath { get; set; }

    /// <summary>Optional override for the ArcFace recognition model path (defaults to the bundled model).</summary>
    public string? RecognitionModelPath { get; set; }

    /// <summary>Number of threads ONNX Runtime may use per inference (0 = runtime default).</summary>
    [Range(0, 64)]
    public int IntraOpNumThreads { get; set; }
}
