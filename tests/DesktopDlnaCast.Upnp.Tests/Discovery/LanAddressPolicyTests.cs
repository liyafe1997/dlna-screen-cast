using System.Net;
using System.Net.NetworkInformation;
using DesktopDlnaCast.Upnp.Discovery;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Discovery;

public sealed class LanAddressPolicyTests
{
    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.20.30.40", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.2", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    public void IsUsableIpv4AppliesLanSafetyRules(string value, bool expected)
    {
        Assert.Equal(expected, LanAddressPolicy.IsUsableIpv4(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Ethernet", "Intel adapter", true)]
    [InlineData(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, "Wi-Fi", "Wireless adapter", true)]
    [InlineData(NetworkInterfaceType.Tunnel, OperationalStatus.Up, "Tunnel", "Adapter", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Down, "Ethernet", "Adapter", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Tailscale", "VPN adapter", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Ethernet", "Hyper-V Virtual Ethernet", true)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Ethernet", "VMware Network Adapter", true)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Ethernet", "VirtualBox Network Adapter", true)]
    public void IsEligibleInterfaceRejectsDisconnectedAndTunnelAdapters(
        NetworkInterfaceType type,
        OperationalStatus status,
        string name,
        string description,
        bool expected)
    {
        Assert.Equal(expected, LanAddressPolicy.IsEligibleInterface(type, status, name, description));
    }
}
