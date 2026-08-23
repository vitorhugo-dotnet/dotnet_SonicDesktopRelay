using Avalonia;
using SonicDesktopRelay.Presentation;
using Xunit;

namespace SonicDesktopRelay.Presentation.Tests;

public sealed class VideoSurfaceGeometryTests
{
    [Theory]
    // 16:9 source into a wider viewport: bars left and right.
    [InlineData(1920, 1080, 1000, 500, 888.88, 500, 55.55, 0)]
    // 16:9 source into a taller viewport: bars top and bottom.
    [InlineData(1920, 1080, 800, 800, 800, 450, 0, 175)]
    // Exact match: no bars.
    [InlineData(1920, 1080, 1920, 1080, 1920, 1080, 0, 0)]
    public void The_picture_is_letterboxed_never_stretched(
        double sourceWidth, double sourceHeight, double availableWidth, double availableHeight,
        double expectedWidth, double expectedHeight, double expectedX, double expectedY)
    {
        var rect = LetterboxGeometry.Fit(
            new Size(sourceWidth, sourceHeight), new Size(availableWidth, availableHeight));

        Assert.Equal(expectedWidth, rect.Width, 1);
        Assert.Equal(expectedHeight, rect.Height, 1);
        Assert.Equal(expectedX, rect.X, 1);
        Assert.Equal(expectedY, rect.Y, 1);
    }

    [Fact]
    public void A_zero_sized_viewport_produces_an_empty_rect_rather_than_dividing_by_zero()
    {
        var rect = LetterboxGeometry.Fit(new Size(1920, 1080), new Size(0, 0));

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    [Fact]
    public void A_source_with_no_pixels_produces_an_empty_rect()
    {
        // A frame is only sized once one has been decoded; until then the surface has to
        // draw nothing rather than divide by a zero height.
        var rect = LetterboxGeometry.Fit(new Size(0, 0), new Size(800, 600));

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    [Fact]
    public void The_picture_is_never_upscaled_past_the_viewport()
    {
        var rect = LetterboxGeometry.Fit(new Size(640, 480), new Size(1920, 1080));

        // Filling the viewport is right; overflowing it is not.
        Assert.True(rect.Width <= 1920);
        Assert.True(rect.Height <= 1080);
        Assert.Equal(1440, rect.Width, 1);
        Assert.Equal(1080, rect.Height, 1);
    }
}
