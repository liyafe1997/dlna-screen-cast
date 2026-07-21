namespace DesktopDlnaCast.Upnp.Description;

public sealed class UpnpDescriptionException : FormatException
{
    public UpnpDescriptionException()
    {
    }

    public UpnpDescriptionException(string message)
        : base(message)
    {
    }

    public UpnpDescriptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
