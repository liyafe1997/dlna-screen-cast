namespace DesktopDlnaCast.Upnp.Soap;

public sealed record SoapFault(
    string? FaultCode,
    string? FaultString,
    int? UpnpErrorCode,
    string? UpnpErrorDescription);

