using FaceMatch.Core.Domain;

namespace FaceMatch.Tests;

public class FaceEmbeddingTests
{
    [Fact]
    public void Normalize_ProducesUnitVector()
    {
        var normalized = FaceEmbedding.Normalize([3f, 4f]);

        Assert.Equal(0.6f, normalized[0], 5);
        Assert.Equal(0.8f, normalized[1], 5);
    }

    [Fact]
    public void Normalize_ZeroVector_StaysZero()
    {
        Assert.All(FaceEmbedding.Normalize(new float[4]), v => Assert.Equal(0f, v));
    }

    [Theory]
    [InlineData(new[] { 1f, 0f }, new[] { 1f, 0f }, 1f)]
    [InlineData(new[] { 1f, 0f }, new[] { 0f, 1f }, 0f)]
    [InlineData(new[] { 1f, 0f }, new[] { -2f, 0f }, -1f)]
    [InlineData(new[] { 1f, 1f }, new[] { 2f, 2f }, 1f)]
    public void CosineSimilarity_MatchesDefinition(float[] a, float[] b, float expected)
    {
        Assert.Equal(expected, FaceEmbedding.CosineSimilarity(a, b), 5);
    }

    [Fact]
    public void CosineSimilarity_RejectsDifferentLengths()
    {
        Assert.Throws<ArgumentException>(() => FaceEmbedding.CosineSimilarity([1f], [1f, 2f]));
    }
}
