using System.Runtime.InteropServices;

namespace DesktopDlnaCast.Media.Interop.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStreamConfiguration
{
    public int StructSize;
    public int AbiVersion;
    public int SourceKind;
    public ulong SourceHandle;
    public int IncludeCursor;
    public int Width;
    public int Height;
    public int FrameRate;
    public int VideoBitrate;
    public int GopFrames;
    public int AudioBitrate;
    public int IncludeAudio;
    public int StreamMode;
    public int MuteLocalPlayback;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSessionStatistics
{
    public int StructSize;
    public int AbiVersion;
    public long CapturedVideoFrames;
    public long EncodedVideoFrames;
    public long DroppedVideoFrames;
    public long CapturedAudioPackets;
    public long EncodedAudioFrames;
    public long AudioDeviceChanges;
    public long QueueOverflows;
    public long TimestampCorrections;
    public long EncoderWait100Nanoseconds;
    public long LocalPlaybackMuteChanges;
    public long LocalPlaybackMuteRestoreFailures;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEncoderDiagnostics
{
    public int StructSize;
    public int AbiVersion;
    public int IsHardware;
    public int AcceptedWidth;
    public int AcceptedHeight;
    public int FrameRateNumerator;
    public int FrameRateDenominator;
    public int AcceptedVideoBitrate;
    public int H264Profile;
    public int AcceptedGopFrames;
    public int AcceptedBFrameCount;
    public int VideoProcessorBackend;
    public int AudioEnabled;
    public int AcceptedAudioBitrate;
    public int AudioSampleRate;
    public int AudioChannels;
}
