namespace DesktopDlnaCast.Core.Models;

public enum CaptureSourceKind
{
    Display,
    Window,
}

public enum AspectRatioMode
{
    Stretch,
    CenterCrop,
    Letterbox,
}

public sealed record DisplayCaptureSource(
    long Handle,
    int Index,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record DisplayPreviewFrame(
    int Width,
    int Height,
    byte[] BgraPixels);

public sealed record MediaCaptureConfiguration(
    CaptureSourceKind SourceKind,
    long SourceHandle,
    bool IncludeCursor,
    int Width,
    int Height,
    int FrameRate,
    int VideoBitrate,
    int GopFrames,
    bool IncludeAudio,
    int AudioBitrate,
    bool MuteLocalPlayback = false,
    bool StartAtLiveEdge = false,
    bool AudioOnly = false,
    AudioCastProfile AudioProfile = AudioCastProfile.None,
    AspectRatioMode AspectRatioMode = AspectRatioMode.Letterbox);

public sealed record MediaSessionStatistics(
    long CapturedVideoFrames,
    long EncodedVideoFrames,
    long DroppedVideoFrames,
    long CapturedAudioPackets,
    long EncodedAudioFrames,
    long AudioDeviceChanges,
    long QueueOverflows,
    long TimestampCorrections,
    TimeSpan EncoderWaitTime,
    long LocalPlaybackMuteChanges,
    long LocalPlaybackMuteRestoreFailures);

public enum MediaVideoProcessorBackend
{
    Unknown,
    D3D11,
    Libswscale,
}

public sealed record MediaEncoderDiagnostics(
    string EncoderName,
    bool IsHardware,
    int AcceptedWidth,
    int AcceptedHeight,
    int FrameRateNumerator,
    int FrameRateDenominator,
    int AcceptedVideoBitrate,
    int H264Profile,
    int AcceptedGopFrames,
    int AcceptedBFrameCount,
    MediaVideoProcessorBackend VideoProcessorBackend,
    bool AudioEnabled,
    int AcceptedAudioBitrate,
    int AudioSampleRate,
    int AudioChannels);
