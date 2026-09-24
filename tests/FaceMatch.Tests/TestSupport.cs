using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Faces;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceMatch.Tests;

internal static class TestSupport
{
    /// <summary>
    /// Directory with the photos from <c>scripts/download-samples.sh</c>. Sample-based tests are skipped when it is missing.
    /// </summary>
    public static string? SamplesDirectory
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("FACEMATCH_SAMPLES");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return Directory.Exists(fromEnv) ? fromEnv : null;
            }

            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "samples");
                if (File.Exists(Path.Combine(dir.FullName, "FaceMatch.slnx")))
                {
                    return File.Exists(Path.Combine(candidate, "obama.jpg")) ? candidate : null;
                }
            }

            return null;
        }
    }

    public static string Sample(string name)
    {
        var dir = SamplesDirectory;
        Skip.If(dir is null, "Sample photos not found. Run ./scripts/download-samples.sh first.");
        return Path.Combine(dir!, name);
    }

    public static FaceAiSharpAnalyzer CreateAnalyzer(FaceRecognitionOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new FaceRecognitionOptions()), NullLogger<FaceAiSharpAnalyzer>.Instance);

    public static MemoryStream SolidPng(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(128, 128, 128));
        var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }
}

/// <summary>Loading the ONNX models takes a moment, so one analyzer is shared by all tests of a class.</summary>
public sealed class AnalyzerFixture : IDisposable
{
    public FaceAiSharpAnalyzer Analyzer { get; } = TestSupport.CreateAnalyzer();

    public void Dispose() => Analyzer.Dispose();
}
