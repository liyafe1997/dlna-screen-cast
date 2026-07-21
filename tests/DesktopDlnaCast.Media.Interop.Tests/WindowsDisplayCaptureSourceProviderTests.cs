using Xunit;

namespace DesktopDlnaCast.Media.Interop.Tests;

public sealed class WindowsDisplayCaptureSourceProviderTests
{
    [Fact]
    public void CreateSourcesOrdersPrimaryFirstAndPreservesEveryValidMonitorHandle()
    {
        IReadOnlyList<Core.Models.DisplayCaptureSource> displays =
            WindowsDisplayCaptureSourceProvider.CreateSources(
            [
                new(22, 1920, 0, 2560, 1440, false),
                new(11, 0, 0, 1920, 1080, true),
                new(0, -1, -1, 0, 0, false),
            ]);

        Assert.Equal(2, displays.Count);
        Assert.Equal(11, displays[0].Handle);
        Assert.True(displays[0].IsPrimary);
        Assert.Equal(1, displays[0].Index);
        Assert.Equal(22, displays[1].Handle);
        Assert.Equal(2560, displays[1].Width);
        Assert.Equal(2, displays[1].Index);
    }
}
