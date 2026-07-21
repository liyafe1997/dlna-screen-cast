using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Description;

public sealed record UpnpServiceDescription(
    UpnpServiceType ServiceType,
    Uri ControlUri,
    Uri? EventSubscriptionUri,
    Uri? ServiceDescriptionUri);

