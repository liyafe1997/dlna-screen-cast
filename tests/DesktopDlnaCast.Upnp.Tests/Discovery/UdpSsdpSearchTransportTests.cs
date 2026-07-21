using DesktopDlnaCast.Upnp.Discovery;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Discovery;

public sealed class UdpSsdpSearchTransportTests
{
    [Fact]
    public void CreateRequestBuildsStandardsCompliantMSearch()
    {
        string request = UdpSsdpSearchTransport.CreateRequest(
            "urn:schemas-upnp-org:device:MediaRenderer:1",
            2);

        Assert.StartsWith("M-SEARCH * HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains("HOST: 239.255.255.250:1900\r\n", request, StringComparison.Ordinal);
        Assert.Contains("MAN: \"ssdp:discover\"\r\n", request, StringComparison.Ordinal);
        Assert.Contains("MX: 2\r\n", request, StringComparison.Ordinal);
        Assert.EndsWith("ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n", request, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ssdp:all\r\nINJECTED: value")]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateRequestRejectsHeaderInjection(string searchTarget)
    {
        Assert.Throws<ArgumentException>(() => UdpSsdpSearchTransport.CreateRequest(searchTarget, 2));
    }
}

