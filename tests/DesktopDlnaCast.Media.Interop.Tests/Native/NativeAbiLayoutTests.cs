using System.Runtime.InteropServices;
using DesktopDlnaCast.Media.Interop.Native;
using Xunit;

namespace DesktopDlnaCast.Media.Interop.Tests.Native;

public sealed class NativeAbiLayoutTests
{
    [Fact]
    public void AbiVersionAndResultCodesMatchNativeHeaderContract()
    {
        Assert.Equal(4, NativeAbi.Version);
        Assert.Equal(0, (int)NativeResult.Success);
        Assert.Equal(1, (int)NativeResult.Timeout);
        Assert.Equal(2, (int)NativeResult.EndOfStream);
        Assert.Equal(-6, (int)NativeResult.Canceled);
        Assert.Equal(-127, (int)NativeResult.InternalFailure);
    }

    [Fact]
    public void StreamConfigurationHasStableSequentialLayout()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(NativeStreamConfiguration).StructLayoutAttribute!.Value);
        Assert.Equal(64, Marshal.SizeOf<NativeStreamConfiguration>());
        Assert.Equal(0, Marshal.OffsetOf<NativeStreamConfiguration>(nameof(NativeStreamConfiguration.StructSize)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NativeStreamConfiguration>(nameof(NativeStreamConfiguration.SourceHandle)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<NativeStreamConfiguration>(nameof(NativeStreamConfiguration.IncludeCursor)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<NativeStreamConfiguration>(nameof(NativeStreamConfiguration.StreamMode)).ToInt32());
        Assert.Equal(60, Marshal.OffsetOf<NativeStreamConfiguration>(nameof(NativeStreamConfiguration.MuteLocalPlayback)).ToInt32());
    }

    [Fact]
    public void StatisticsHasStableSequentialLayout()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(NativeSessionStatistics).StructLayoutAttribute!.Value);
        Assert.Equal(96, Marshal.SizeOf<NativeSessionStatistics>());
        Assert.Equal(8, Marshal.OffsetOf<NativeSessionStatistics>(nameof(NativeSessionStatistics.CapturedVideoFrames)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<NativeSessionStatistics>(nameof(NativeSessionStatistics.CapturedAudioPackets)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<NativeSessionStatistics>(nameof(NativeSessionStatistics.EncoderWait100Nanoseconds)).ToInt32());
        Assert.Equal(80, Marshal.OffsetOf<NativeSessionStatistics>(nameof(NativeSessionStatistics.LocalPlaybackMuteChanges)).ToInt32());
        Assert.Equal(88, Marshal.OffsetOf<NativeSessionStatistics>(nameof(NativeSessionStatistics.LocalPlaybackMuteRestoreFailures)).ToInt32());
    }

    [Fact]
    public void EncoderDiagnosticsHasStableSequentialLayout()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(NativeEncoderDiagnostics).StructLayoutAttribute!.Value);
        Assert.Equal(64, Marshal.SizeOf<NativeEncoderDiagnostics>());
        Assert.Equal(0, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.StructSize)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.AcceptedWidth)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.AcceptedVideoBitrate)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.AcceptedBFrameCount)).ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.VideoProcessorBackend)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.AudioEnabled)).ToInt32());
        Assert.Equal(60, Marshal.OffsetOf<NativeEncoderDiagnostics>(nameof(NativeEncoderDiagnostics.AudioChannels)).ToInt32());
    }
}
