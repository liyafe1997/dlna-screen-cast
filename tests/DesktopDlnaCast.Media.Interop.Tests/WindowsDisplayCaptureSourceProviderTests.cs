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

    [Fact]
    public void CreateWindowSourcesFiltersInvalidEntriesAndOrdersWindowsByTitle()
    {
        IReadOnlyList<Core.Models.DisplayCaptureSource> windows =
            WindowsDisplayCaptureSourceProvider.CreateWindowSources(
            [
                new(22, "Zulu", 10, 20, 1280, 720),
                new(11, "Alpha", 0, 0, 800, 600),
                new(0, "Invalid", 0, 0, 800, 600),
                new(33, " ", 0, 0, 800, 600),
                new(22, "Duplicate", 10, 20, 1280, 720),
            ]);

        Assert.Equal(2, windows.Count);
        Assert.Equal(Core.Models.CaptureSourceKind.Window, windows[0].Kind);
        Assert.Equal("Alpha", windows[0].Title);
        Assert.Equal(11, windows[0].Handle);
        Assert.Equal("Zulu", windows[1].Title);
        Assert.False(windows[1].IsPrimary);
    }
}
