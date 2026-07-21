namespace DesktopDlnaCast.Media.Interop;

public sealed class NativeMediaException(int resultCode, string message) : Exception(message)
{
    public int ResultCode { get; } = resultCode;
}
