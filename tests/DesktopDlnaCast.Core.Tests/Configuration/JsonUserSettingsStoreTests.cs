using DesktopDlnaCast.Core.Configuration;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopDlnaCast.Core.Tests.Configuration;

public sealed class JsonUserSettingsStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "DesktopDlnaCast.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingFileReturnsDefaults()
    {
        using JsonUserSettingsStore store = CreateStore();

        UserSettings settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1280, settings.OutputWidth);
        Assert.Equal(720, settings.OutputHeight);
        Assert.True(settings.IncludeCursor);
        Assert.True(settings.IncludeAudio);
        Assert.False(settings.AudioOnly);
        Assert.False(settings.MuteLocalPlayback);
        Assert.Equal(AspectRatioMode.Letterbox, settings.AspectRatioMode);
        Assert.Null(settings.Display);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsEverySelection()
    {
        using JsonUserSettingsStore store = CreateStore();
        UserSettings expected = new()
        {
            OutputWidth = 1920,
            OutputHeight = 1080,
            IncludeCursor = false,
            IncludeAudio = true,
            AudioOnly = true,
            MuteLocalPlayback = true,
            RendererUdn = "uuid:test-renderer",
            Display = new(2, 1920, 0, 2560, 1440, false),
        };

        await store.SaveAsync(expected, CancellationToken.None);
        UserSettings actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task InvalidJsonReturnsDefaults()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(GetSettingsPath(), "{not-json", CancellationToken.None);
        using JsonUserSettingsStore store = CreateStore();

        UserSettings settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(new UserSettings(), settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private JsonUserSettingsStore CreateStore() => new(
        GetSettingsPath(),
        NullLogger<JsonUserSettingsStore>.Instance);

    private string GetSettingsPath() => Path.Combine(testDirectory, "user-settings.json");
}
