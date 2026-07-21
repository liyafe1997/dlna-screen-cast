namespace DesktopDlnaCast.Media.Interop.Native;

internal enum NativeResult
{
    Success = 0,
    Timeout = 1,
    EndOfStream = 2,
    InvalidArgument = -1,
    InvalidState = -2,
    PlatformFailure = -3,
    MediaPipelineFailure = -4,
    BufferTooSmall = -5,
    Canceled = -6,
    InternalFailure = -127,
}
