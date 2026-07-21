namespace DesktopDlnaCast.Core.Casting;

public sealed class RendererCommandException : Exception
{
    public RendererCommandException()
    {
    }

    public RendererCommandException(string message)
        : base(message)
    {
    }

    public RendererCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RendererCommandException(
        string message,
        int? httpStatus,
        int? upnpErrorCode,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
        UpnpErrorCode = upnpErrorCode;
    }

    public int? HttpStatus { get; }

    public int? UpnpErrorCode { get; }
}
