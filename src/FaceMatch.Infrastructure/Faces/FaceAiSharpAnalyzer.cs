using FaceAiSharp;
using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceMatch.Infrastructure.Faces;

/// <summary>
/// Face pipeline built on FaceAiSharp:
/// SCRFD (detection + 5 landmarks) → similarity-transform alignment to 112×112 → ArcFace ResNet100 (512-d embedding).
/// Both ONNX sessions are thread-safe, so a single instance is shared by the whole application.
/// </summary>
public sealed class FaceAiSharpAnalyzer : IFaceAnalyzer, IDisposable
{
    private const string DefaultDetectorModel = "scrfd_2.5g_kps.onnx";
    private const string DefaultRecognitionModel = "arcfaceresnet100-11-int8.onnx";

    private static readonly JpegEncoder ThumbnailEncoder = new() { Quality = 85 };

    private readonly FaceRecognitionOptions _options;
    private readonly ILogger<FaceAiSharpAnalyzer> _logger;
    private readonly ScrfdDetector _detector;
    private readonly ArcFaceEmbeddingsGenerator _embedder;

    public FaceAiSharpAnalyzer(IOptions<FaceRecognitionOptions> options, ILogger<FaceAiSharpAnalyzer> logger)
    {
        _options = options.Value;
        _logger = logger;

        var detectorPath = ResolveModelPath(_options.DetectorModelPath, DefaultDetectorModel);
        var recognitionPath = ResolveModelPath(_options.RecognitionModelPath, DefaultRecognitionModel);

        var maxInput = RoundToMultipleOf32(_options.DetectorMaxInputSize);
        _detector = new ScrfdDetector(
            new ScrfdDetectorOptions
            {
                ModelPath = detectorPath,
                AutoResizeInputToModelDimensions = true,
                ConfidenceThreshold = _options.DetectionConfidenceThreshold,
                MaximumInputSize = new Size(maxInput, maxInput),
            },
            CreateSessionOptions());

        _embedder = new ArcFaceEmbeddingsGenerator(
            new ArcFaceEmbeddingsGeneratorOptions
            {
                ModelPath = recognitionPath,
                AutoResizeInputToModelDimensions = true,
            },
            CreateSessionOptions());

        _logger.LogInformation(
            "Face models loaded (detector: {Detector}, recognition: {Recognition}, detector input: {Input}px)",
            Path.GetFileName(detectorPath),
            Path.GetFileName(recognitionPath),
            maxInput);
    }

    public int EmbeddingSize => FaceEmbedding.Dimensions;

    public ImageAnalysis Analyze(Stream image, bool includeThumbnails, CancellationToken cancellationToken = default)
    {
        using var img = Decode(image);
        cancellationToken.ThrowIfCancellationRequested();

        var detections = _detector.DetectFaces(img);
        var faces = new List<AnalyzedFace>(detections.Count);
        var bounds = new RectangleF(0, 0, img.Width, img.Height);

        foreach (var detection in detections.OrderByDescending(d => d.Confidence ?? 0))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var confidence = detection.Confidence ?? 0;
            if (confidence < _options.DetectionConfidenceThreshold || detection.Landmarks is not { Count: 5 })
            {
                continue;
            }

            var box = RectangleF.Intersect(detection.Box, bounds);
            if (Math.Min(box.Width, box.Height) < _options.MinFaceSize)
            {
                continue;
            }

            var embedding = Embed(img, box, detection.Landmarks);
            var thumbnail = includeThumbnails ? RenderThumbnail(img, box) : null;

            faces.Add(new AnalyzedFace(new FaceBox(box.X, box.Y, box.Width, box.Height), confidence, embedding, thumbnail));
        }

        return new ImageAnalysis(img.Width, img.Height, faces);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _embedder.Dispose();
    }

    private Image<Rgb24> Decode(Stream stream)
    {
        try
        {
            var info = Image.Identify(stream);
            if ((long)info.Width * info.Height > _options.MaxImagePixels)
            {
                throw new InvalidImageException(
                    $"Image is too large ({info.Width}x{info.Height}); the limit is {_options.MaxImagePixels:N0} pixels.");
            }

            stream.Position = 0;
            var img = Image.Load<Rgb24>(stream);

            // Phones store rotation in EXIF; bake it in so boxes match what users see.
            img.Mutate(x => x.AutoOrient());
            return img;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new InvalidImageException("The file is not a supported image.", ex);
        }
    }

    /// <summary>
    /// Aligns and embeds a single face. To keep this cheap on large group photos, the face is first cropped with a
    /// generous margin and the landmarks are shifted into the crop, instead of warping the whole image.
    /// </summary>
    private float[] Embed(Image<Rgb24> img, RectangleF box, IReadOnlyList<PointF> landmarks)
    {
        var region = ExpandSquare(box, 2.0f, img.Width, img.Height);
        using var crop = img.Clone(x => x.Crop(region));

        var shifted = landmarks.Select(p => new PointF(p.X - region.X, p.Y - region.Y)).ToArray();
        ((IFaceEmbeddingsGenerator)_embedder).AlignFaceUsingLandmarks(crop, shifted);

        return FaceEmbedding.Normalize(_embedder.GenerateEmbedding(crop));
    }

    private byte[] RenderThumbnail(Image<Rgb24> img, RectangleF box)
    {
        var region = ExpandSquare(box, 1.4f, img.Width, img.Height);
        using var thumb = img.Clone(x => x
            .Crop(region)
            .Resize(new ResizeOptions
            {
                Size = new Size(_options.ThumbnailSize, _options.ThumbnailSize),
                Mode = ResizeMode.Pad,
                PadColor = Color.Black,
            }));

        using var ms = new MemoryStream();
        thumb.Save(ms, ThumbnailEncoder);
        return ms.ToArray();
    }

    /// <summary>Returns a square around the box centre, <paramref name="scale"/> times its longest side, clamped to the image.</summary>
    internal static Rectangle ExpandSquare(RectangleF box, float scale, int imageWidth, int imageHeight)
    {
        var side = Math.Max(box.Width, box.Height) * scale;
        var cx = box.X + (box.Width / 2);
        var cy = box.Y + (box.Height / 2);

        var left = (int)Math.Floor(Math.Max(0, cx - (side / 2)));
        var top = (int)Math.Floor(Math.Max(0, cy - (side / 2)));
        var right = (int)Math.Ceiling(Math.Min(imageWidth, cx + (side / 2)));
        var bottom = (int)Math.Ceiling(Math.Min(imageHeight, cy + (side / 2)));

        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private SessionOptions CreateSessionOptions()
    {
        var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (_options.IntraOpNumThreads > 0)
        {
            so.IntraOpNumThreads = _options.IntraOpNumThreads;
        }

        return so;
    }

    private static int RoundToMultipleOf32(int value) => Math.Max(32, value / 32 * 32);

    private static string ResolveModelPath(string? configured, string defaultFileName)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "onnx", defaultFileName)
            : configured;

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Face model not found at '{path}'. The FaceAiSharp.Bundle package copies models to the 'onnx' folder of the output directory.",
                path);
        }

        return path;
    }
}
