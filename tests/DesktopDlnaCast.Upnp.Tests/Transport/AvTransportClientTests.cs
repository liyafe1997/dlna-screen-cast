using System.Net;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Tests.Soap;
using DesktopDlnaCast.Upnp.Transport;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Transport;

public sealed class AvTransportClientTests
{
    private static readonly UpnpServiceType ServiceType = new("schemas-upnp-org", "AVTransport", 2);
    private static readonly UpnpServiceDescription Service = new(
        ServiceType,
        new("http://192.168.1.42:1400/control"),
        null,
        null);

    [Fact]
    public async Task GetTransportInfoReadsStateStatusAndSpeed()
    {
        const string content =
            "<CurrentTransportState>PLAYING</CurrentTransportState>" +
            "<CurrentTransportStatus>OK</CurrentTransportStatus>" +
            "<CurrentSpeed>1</CurrentSpeed>";
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(UpnpSoapClientTests.CreateResponse(
                HttpStatusCode.OK,
                UpnpSoapClientTests.CreateActionResponse("GetTransportInfo", ServiceType, content)))));
        AvTransportClient client = new(new UpnpSoapClient(httpClient));

        RendererTransportInfo info = await client.GetTransportInfoAsync(Service, CancellationToken.None);

        Assert.Equal("PLAYING", info.CurrentTransportState);
        Assert.Equal("OK", info.CurrentTransportStatus);
        Assert.Equal("1", info.CurrentSpeed);
    }
}

