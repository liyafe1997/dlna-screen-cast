using DesktopDlnaCast.NativeCastProbe;
using Xunit;

namespace DesktopDlnaCast.Media.Interop.Tests.NativeCastProbe;

public sealed class CastProbeOptionsTests
{
    [Fact]
    public void DefaultsDescribeCompatibilitySmokeRun()
    {
        CastProbeOptions options = CastProbeOptions.Parse([]);

        Assert.Equal(TimeSpan.FromSeconds(10), options.Duration);
        Assert.Equal(1, options.Iterations);
        Assert.Equal(1280, options.Width);
        Assert.Equal(720, options.Height);
        Assert.Equal(3_000_000, options.VideoBitrate);
        Assert.Equal(128_000, options.AudioBitrate);
        Assert.True(options.IncludeCursor);
        Assert.True(options.IncludeAudio);
        Assert.False(options.MuteLocalPlayback);
        Assert.False(options.RejectMetadata);
    }

    [Fact]
    public void LocalMuteRequiresAudioAndCanBeEnabled()
    {
        CastProbeOptions options = CastProbeOptions.Parse(["--mute-local-playback", "true"]);

        Assert.True(options.MuteLocalPlayback);
        Assert.Throws<ArgumentException>(() => CastProbeOptions.Parse(
            ["--include-audio", "false", "--mute-local-playback", "true"]));
    }

    [Fact]
    public void ParsesTwentyCycleMetadataFallbackRun()
    {
        CastProbeOptions options = CastProbeOptions.Parse(
            ["--duration-seconds", "2", "--iterations", "20", "--reject-metadata"]);

        Assert.Equal(TimeSpan.FromSeconds(2), options.Duration);
        Assert.Equal(20, options.Iterations);
        Assert.True(options.RejectMetadata);
    }

    [Theory]
    [InlineData("--iterations", "0")]
    [InlineData("--iterations", "21")]
    [InlineData("--duration-seconds", "181")]
    [InlineData("--video-bitrate", "12000001")]
    [InlineData("--audio-bitrate", "512001")]
    public void RejectsUnboundedRuns(string argument, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => CastProbeOptions.Parse([argument, value]));
    }
}
