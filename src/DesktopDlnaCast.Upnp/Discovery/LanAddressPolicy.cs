using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DesktopDlnaCast.Upnp.Discovery;

public static class LanAddressPolicy
{
    private static readonly string[] ExcludedInterfaceMarkers =
    [
        "vpn",
        "tun",
        "tap",
        "tunnel",
        "wireguard",
        "openvpn",
        "zerotier",
        "tailscale",
        "loopback",
    ];

    public static bool IsEligibleInterface(
        NetworkInterfaceType interfaceType,
        OperationalStatus status,
        string name,
        string description)
    {
        if (status != OperationalStatus.Up ||
            interfaceType is NetworkInterfaceType.Loopback or
                NetworkInterfaceType.Tunnel or
                NetworkInterfaceType.Ppp)
        {
            return false;
        }

        string identity = $"{name} {description}";
        return !ExcludedInterfaceMarkers.Any(marker =>
            identity.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUsableIpv4(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] != 169 || bytes[1] != 254;
    }
}
