using System.Text;
using DesktopDlnaCast.Upnp.Discovery;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Discovery;

public sealed class SsdpResponseParserTests
{
    private const string ValidResponse =
        "HTTP/1.1 200 OK\r\n" +
        "CACHE-CONTROL: max-age=1800\r\n" +
        "LOCATION: http://192.168.1.42:1400/device.xml\r\n" +
        "SERVER: TestOS/1.0 UPnP/1.1 MockRenderer/1.0\r\n" +
        "EXT:\r\n" +
        "BOOTID.UPNP.ORG: 0\r\n" +
        "CONFIGID.UPNP.ORG: 13170\r\n" +
        "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n" +
        "USN: uuid:desktop-dlna-cast-test::urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n";

    [Fact]
    public void TryParseReadsRequiredFieldsAndHeadersCaseInsensitively()
    {
        bool parsed = TryParse(ValidResponse, out SsdpResponse? response, out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(response);
        Assert.Equal("uuid:desktop-dlna-cast-test", response.Udn);
        Assert.Equal("uuid:desktop-dlna-cast-test::urn:schemas-upnp-org:device:MediaRenderer:1", response.Usn);
        Assert.Equal(new Uri("http://192.168.1.42:1400/device.xml"), response.Location);
        Assert.Equal("TestOS/1.0 UPnP/1.1 MockRenderer/1.0", response.Server);
        Assert.Equal("max-age=1800", response.Headers["cache-control"]);
        Assert.Equal(string.Empty, response.Headers["ext"]);
        Assert.Equal("0", response.Headers["bootid.upnp.org"]);
    }

    [Theory]
    [InlineData("HTTP/1.1 500 Error\r\nLOCATION: http://192.168.1.1/a\r\nUSN: uuid:x\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nUSN: uuid:x\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: file:///c:/device.xml\r\nUSN: uuid:x\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: http://user:password@192.168.1.1/a\r\nUSN: uuid:x\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.1/a\r\nUSN: not-a-uuid\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.1/a\r\nLOCATION: http://192.168.1.2/a\r\nUSN: uuid:x\r\n\r\n")]
    public void TryParseRejectsMalformedOrUnsafeResponses(string input)
    {
        Assert.False(TryParse(input, out _, out _));
    }

    [Fact]
    public void TryParseRejectsOversizedDatagrams()
    {
        byte[] datagram = new byte[SsdpResponseParser.MaximumDatagramBytes + 1];

        Assert.False(SsdpResponseParser.TryParse(datagram, out _, out _));
    }

    [Fact]
    public void TryParseRejectsEmbeddedControlCharacters()
    {
        byte[] datagram = Encoding.ASCII.GetBytes(ValidResponse);
        datagram[10] = 0;

        Assert.False(SsdpResponseParser.TryParse(datagram, out _, out _));
    }

    [Theory]
    [InlineData("uuid:device", "uuid:device")]
    [InlineData("UUID:DEVICE::urn:schemas-upnp-org:service:AVTransport:1", "UUID:DEVICE")]
    public void UsnParserExtractsStableDeduplicationKey(string usn, string expectedUdn)
    {
        Assert.True(SsdpUsn.TryGetUdn(usn, out string udn));
        Assert.Equal(expectedUdn, udn);
    }

    private static bool TryParse(string value, out SsdpResponse? response, out string? error) =>
        SsdpResponseParser.TryParse(Encoding.ASCII.GetBytes(value), out response, out error);
}
