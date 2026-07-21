using System.Net;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed record SsdpDatagram(
    byte[] Payload,
    IPEndPoint RemoteEndPoint,
    IPAddress LocalAddress);

