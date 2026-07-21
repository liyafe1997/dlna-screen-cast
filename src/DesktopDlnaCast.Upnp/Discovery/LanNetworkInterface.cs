using System.Net;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed record LanNetworkInterface(
    string Id,
    string Name,
    IPAddress Address,
    int InterfaceIndex);

