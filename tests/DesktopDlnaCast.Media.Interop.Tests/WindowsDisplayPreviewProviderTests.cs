using DesktopDlnaCast.Media.Interop;
using Xunit;

namespace DesktopDlnaCast.Media.Interop.Tests;

public sealed class WindowsDisplayPreviewProviderTests
{
    [Theory]
    [InlineData(1920, 1080, 240, 135)]
    [InlineData(2560, 1440, 240, 135)]
    [InlineData(1024, 1280, 108, 135)]
    [InlineData(3840, 1080, 240, 68)]
    public void CalculatePreviewSizePreservesAspectRatioWithinBounds(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        (int width, int height) =
            WindowsDisplayPreviewProvider.CalculatePreviewSize(sourceWidth, sourceHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
        Assert.InRange(width, 1, WindowsDisplayPreviewProvider.MaximumPreviewWidth);
        Assert.InRange(height, 1, WindowsDisplayPreviewProvider.MaximumPreviewHeight);
    }
}
