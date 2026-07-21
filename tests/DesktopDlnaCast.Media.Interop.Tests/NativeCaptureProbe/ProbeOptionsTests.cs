using DesktopDlnaCast.NativeCaptureProbe;
using Xunit;

namespace DesktopDlnaCast.Media.Interop.Tests.NativeCaptureProbe;

public sealed class ProbeOptionsTests
{
    [Fact]
    public void HelpDoesNotRequireOutput()
    {
        ProbeOptions options = ProbeOptions.Parse(["--help"]);

        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void OutputIsRequired()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ProbeOptions.Parse([]));

        Assert.Contains("--output is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsAreBoundedAndCompatible()
    {
        string output = UniqueOutputPath();

        ProbeOptions options = ProbeOptions.Parse(["--output", output]);

        Assert.Equal(Path.GetFullPath(output), options.OutputPath);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Duration);
        Assert.Equal(1280, options.Width);
        Assert.Equal(720, options.Height);
        Assert.Equal(3_000_000, options.VideoBitrate);
        Assert.Equal(128_000, options.AudioBitrate);
        Assert.True(options.IncludeCursor);
        Assert.True(options.IncludeAudio);
        Assert.False(options.MuteLocalPlayback);
        Assert.False(options.Overwrite);
    }

    [Fact]
    public void LocalMuteRequiresAudioAndCanBeEnabled()
    {
        string output = UniqueOutputPath();

        ProbeOptions options = ProbeOptions.Parse(
            ["--output", output, "--mute-local-playback", "true"]);

        Assert.True(options.MuteLocalPlayback);
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(
            ["--output", output, "--include-audio", "false", "--mute-local-playback", "true"]));
    }

    [Theory]
    [InlineData("--duration-seconds", "0")]
    [InlineData("--duration-seconds", "601")]
    [InlineData("--width", "1279")]
    [InlineData("--height", "721")]
    [InlineData("--video-bitrate", "99999")]
    [InlineData("--audio-bitrate", "31999")]
    public void RejectsOutOfBoundsValues(string argument, string value)
    {
        string output = UniqueOutputPath();

        Assert.ThrowsAny<ArgumentException>(() =>
            ProbeOptions.Parse(["--output", output, argument, value]));
    }

    [Fact]
    public void ExistingOutputRequiresExplicitOverwrite()
    {
        string output = UniqueOutputPath();
        File.WriteAllBytes(output, [0x47]);
        try
        {
            Assert.Throws<IOException>(() => ProbeOptions.Parse(["--output", output]));

            ProbeOptions options = ProbeOptions.Parse(["--output", output, "--overwrite"]);
            Assert.True(options.Overwrite);
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static string UniqueOutputPath() =>
        Path.Combine(Path.GetTempPath(), $"desktop-dlna-capture-{Guid.NewGuid():N}.ts");
}
