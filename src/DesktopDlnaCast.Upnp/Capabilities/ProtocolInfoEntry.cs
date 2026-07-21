namespace DesktopDlnaCast.Upnp.Capabilities;

public sealed record ProtocolInfoEntry(
    string Transport,
    string Network,
    string ContentFormat,
    string AdditionalInfo);

