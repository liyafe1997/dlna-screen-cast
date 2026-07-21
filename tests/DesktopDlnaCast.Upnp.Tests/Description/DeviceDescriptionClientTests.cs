using System.Net;
using System.Text;
using DesktopDlnaCast.Upnp.Description;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Description;

public sealed class DeviceDescriptionClientTests
{
    private static readonly Uri DescriptionUri = new("http://192.168.1.42:1400/device.xml");

    [Fact]
    public async Task GetAsyncDownloadsAndParsesBoundedDescription()
    {
        HttpMethod? observedMethod = null;
        using HttpClient httpClient = new(new TestHttpMessageHandler((request, _) =>
        {
            observedMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateDescription(), Encoding.UTF8, "text/xml"),
            });
        }));
        DeviceDescriptionClient client = new(httpClient);

        RendererDeviceDescription description = await client.GetAsync(DescriptionUri, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, observedMethod);
        Assert.Equal("uuid:http-test", description.Udn);
        Assert.Equal("HTTP Test Renderer", description.FriendlyName);
    }

    [Fact]
    public async Task GetAsyncRejectsContentLengthOverLimitBeforeReading()
    {
        byte[] oversized = new byte[DeviceDescriptionClient.MaximumResponseBytes + 1];
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(oversized),
            })));
        DeviceDescriptionClient client = new(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(DescriptionUri, CancellationToken.None));
    }

    private static string CreateDescription() =>
        """
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>HTTP Test Renderer</friendlyName>
            <UDN>uuid:http-test</UDN>
          </device>
        </root>
        """;
}

