using DesktopDlnaCast.Upnp.Services;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Services;

public sealed class UpnpServiceTypeTests
{
    [Theory]
    [InlineData("urn:schemas-upnp-org:service:AVTransport:1", "AVTransport", 1)]
    [InlineData("urn:schemas-upnp-org:service:AVTransport:3", "AVTransport", 3)]
    [InlineData("urn:schemas-upnp-org:service:ConnectionManager:2", "ConnectionManager", 2)]
    public void TryParseRecognizesSupportedServiceVersions(string value, string expectedName, int expectedVersion)
    {
        bool parsed = UpnpServiceType.TryParse(value, out UpnpServiceType serviceType);

        Assert.True(parsed);
        Assert.Equal(expectedName, serviceType.Name);
        Assert.Equal(expectedVersion, serviceType.Version);
        Assert.Equal(value, serviceType.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("urn:schemas-upnp-org:device:MediaRenderer:1")]
    [InlineData("urn:schemas-upnp-org:service:AVTransport:0")]
    [InlineData("urn:schemas-upnp-org:service:AVTransport:not-a-version")]
    [InlineData("http://schemas-upnp-org/service/AVTransport/1")]
    public void TryParseRejectsMalformedValues(string? value)
    {
        Assert.False(UpnpServiceType.TryParse(value, out _));
    }
}
