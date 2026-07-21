using DesktopDlnaCast.Upnp.Description;

namespace DesktopDlnaCast.Upnp.Control;

internal sealed record RendererControlContext(
    RendererDeviceDescription Description,
    UpnpServiceDescription AvTransport,
    UpnpServiceDescription? ConnectionManager,
    UpnpServiceDescription? RenderingControl);

