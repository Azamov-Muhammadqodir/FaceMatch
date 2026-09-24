using FaceMatch.Infrastructure.Faces;
using SixLabors.ImageSharp;

namespace FaceMatch.Tests;

public class ExpandSquareTests
{
    [Fact]
    public void ExpandsAroundCentre()
    {
        var region = FaceAiSharpAnalyzer.ExpandSquare(new RectangleF(100, 100, 50, 100), 2f, 1000, 1000);

        // Longest side 100 * 2 = 200, centred on (125, 150).
        Assert.Equal(Rectangle.FromLTRB(25, 50, 225, 250), region);
    }

    [Fact]
    public void ClampsToImageBounds()
    {
        var region = FaceAiSharpAnalyzer.ExpandSquare(new RectangleF(0, 0, 40, 40), 3f, 100, 50);

        Assert.True(region.Left >= 0 && region.Top >= 0);
        Assert.True(region.Right <= 100 && region.Bottom <= 50);
        Assert.False(region.IsEmpty);
    }
}
