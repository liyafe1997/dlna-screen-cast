using System.Net;
using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Rendering;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Tests.Soap;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Rendering;

public sealed class RenderingControlClientTests
{
    private static readonly UpnpServiceType ServiceType = new("schemas-upnp-org", "RenderingControl", 1);
    private static readonly UpnpServiceDescription Service = new(
        ServiceType,
        new("http://192.168.1.42:1400/renderingcontrol/control"),
        null,
        null);

    [Fact]
    public async Task GetVolumeReadsCurrentVolume()
    {
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(UpnpSoapClientTests.CreateResponse(
                HttpStatusCode.OK,
                UpnpSoapClientTests.CreateActionResponse(
                    "GetVolume",
                    ServiceType,
                    "<CurrentVolume>37</CurrentVolume>")))));
        RenderingControlClient client = new(new UpnpSoapClient(httpClient));

        int volume = await client.GetVolumeAsync(Service, CancellationToken.None);

        Assert.Equal(37, volume);
    }

    [Fact]
    public async Task GetVolumeRejectsNonNumericCurrentVolume()
    {
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(UpnpSoapClientTests.CreateResponse(
                HttpStatusCode.OK,
                UpnpSoapClientTests.CreateActionResponse(
                    "GetVolume",
                    ServiceType,
                    "<CurrentVolume>loud</CurrentVolume>")))));
        RenderingControlClient client = new(new UpnpSoapClient(httpClient));

        await Assert.ThrowsAsync<FormatException>(() =>
            client.GetVolumeAsync(Service, CancellationToken.None));
    }

    [Fact]
    public async Task SetVolumeSendsMasterChannelAndDesiredVolume()
    {
        string? observedBody = null;
        using HttpClient httpClient = new(new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return UpnpSoapClientTests.CreateResponse(
                HttpStatusCode.OK,
                UpnpSoapClientTests.CreateActionResponse("SetVolume", ServiceType));
        }));
        RenderingControlClient client = new(new UpnpSoapClient(httpClient));

        await client.SetVolumeAsync(Service, 55, CancellationToken.None);

        XDocument requestDocument = XDocument.Parse(observedBody!);
        Assert.Equal(
            "Master",
            requestDocument.Descendants().Single(element => element.Name.LocalName == "Channel").Value);
        Assert.Equal(
            "55",
            requestDocument.Descendants().Single(element => element.Name.LocalName == "DesiredVolume").Value);
    }

    [Fact]
    public async Task SetVolumeRejectsNegativeVolume() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new RenderingControlClient(new UpnpSoapClient(new HttpClient()))
                .SetVolumeAsync(Service, -1, CancellationToken.None));
}
