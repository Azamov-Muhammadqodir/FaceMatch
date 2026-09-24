using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;

namespace FaceMatch.Tests;

public class FaceAnalyzerTests(AnalyzerFixture fixture) : IClassFixture<AnalyzerFixture>
{
    private readonly IFaceAnalyzer _analyzer = fixture.Analyzer;

    [Fact]
    public void ImageWithoutFaces_ReturnsNoFaces()
    {
        using var png = TestSupport.SolidPng(640, 480);

        var analysis = _analyzer.Analyze(png, includeThumbnails: true);

        Assert.Equal(640, analysis.Width);
        Assert.Equal(480, analysis.Height);
        Assert.Empty(analysis.Faces);
    }

    [Fact]
    public void NonImageContent_Throws()
    {
        using var garbage = new MemoryStream("definitely not an image"u8.ToArray());

        Assert.Throws<InvalidImageException>(() => _analyzer.Analyze(garbage, includeThumbnails: false));
    }

    [Fact]
    public void ImageAbovePixelLimit_IsRejectedBeforeDecoding()
    {
        using var analyzer = TestSupport.CreateAnalyzer(new FaceRecognitionOptions { MaxImagePixels = 1_000_000 });
        using var png = TestSupport.SolidPng(1200, 1000);

        var ex = Assert.Throws<InvalidImageException>(() => analyzer.Analyze(png, includeThumbnails: false));
        Assert.Contains("too large", ex.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void GroupPhoto_FindsEveryFace_WithNormalizedEmbeddings()
    {
        using var stream = File.OpenRead(TestSupport.Sample("group.jpg"));

        var analysis = _analyzer.Analyze(stream, includeThumbnails: true);

        Assert.Equal(6, analysis.Faces.Count);
        Assert.All(analysis.Faces, face =>
        {
            Assert.Equal(FaceEmbedding.Dimensions, face.Embedding.Length);
            Assert.Equal(1f, MathF.Sqrt(face.Embedding.Sum(v => v * v)), 3);
            Assert.NotNull(face.ThumbnailJpeg);
            Assert.InRange(face.Box.X, 0, analysis.Width);
            Assert.InRange(face.Box.Y, 0, analysis.Height);
        });
    }

    [SkippableTheory]
    [InlineData("obama.jpg", "obama2.jpg")]
    [InlineData("obama.jpg", "obama3.jpg")]
    [InlineData("kit_harington.jpeg", "kit_with_rose.jpg")]
    [InlineData("alex_lacamoire1.jpg", "alex_lacamoire2.jpg")]
    public void SamePerson_InDifferentPhotos_IsSimilar(string first, string second)
    {
        var a = LargestFace(first);
        var best = AllFaces(second).Max(f => FaceEmbedding.CosineSimilarity(a, f));

        Assert.True(best > 0.55f, $"Expected {first} and {second} to match, similarity was {best:F3}.");
    }

    [SkippableTheory]
    [InlineData("obama.jpg", "biden.jpg")]
    [InlineData("obama.jpg", "rose_leslie.jpg")]
    [InlineData("kit_harington.jpeg", "alex_lacamoire1.jpg")]
    [InlineData("biden.jpg", "group.jpg")]
    public void DifferentPeople_AreNotSimilar(string first, string second)
    {
        var a = LargestFace(first);
        var best = AllFaces(second).Max(f => FaceEmbedding.CosineSimilarity(a, f));

        Assert.True(best < 0.3f, $"Expected {first} and {second} not to match, similarity was {best:F3}.");
    }

    private float[] LargestFace(string sample) =>
        Analyze(sample).Faces.OrderByDescending(f => f.Box.Area).First().Embedding;

    private IEnumerable<float[]> AllFaces(string sample) => Analyze(sample).Faces.Select(f => f.Embedding);

    private ImageAnalysis Analyze(string sample)
    {
        using var stream = File.OpenRead(TestSupport.Sample(sample));
        var analysis = _analyzer.Analyze(stream, includeThumbnails: false);
        Assert.NotEmpty(analysis.Faces);
        return analysis;
    }
}
