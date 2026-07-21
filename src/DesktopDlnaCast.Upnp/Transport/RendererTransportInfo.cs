namespace DesktopDlnaCast.Upnp.Transport;

public sealed record RendererTransportInfo(
    string CurrentTransportState,
    string? CurrentTransportStatus,
    string? CurrentSpeed);

