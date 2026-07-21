using System.Net;
using DesktopDlnaCast.Upnp.Capabilities;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Tests.Soap;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Capabilities;

public sealed class ConnectionManagerClientTests
{
    [Fact]
    public async Task GetSinkProtocolInfoAsyncReturnsParsedSinkEntries()
    {
        UpnpServiceType serviceType = new("schemas-upnp-org", "ConnectionManager", 3);
        UpnpServiceDescription service = new(
            serviceType,
            new("http://192.168.1.42:1400/connection/control"),
            null,
            null);
        const string content =
            "<Source></Source><Sink>http-get:*:video/mpeg:DLNA.ORG_PN=MPEG_TS_HD_NA_ISO</Sink>";
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(UpnpSoapClientTests.CreateResponse(
                HttpStatusCode.OK,
                UpnpSoapClientTests.CreateActionResponse("GetProtocolInfo", serviceType, content)))));
        ConnectionManagerClient client = new(new UpnpSoapClient(httpClient));

        IReadOnlyList<ProtocolInfoEntry> entries = await client.GetSinkProtocolInfoAsync(
            service,
            CancellationToken.None);

        ProtocolInfoEntry entry = Assert.Single(entries);
        Assert.Equal("video/mpeg", entry.ContentFormat);
    }
}
