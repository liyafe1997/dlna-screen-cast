using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Metadata;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Metadata;

public sealed class DidlLiteWriterTests
{
    [Fact]
    public void CreateVideoItemProducesMatchingEscapedMetadata()
    {
        Uri uri = new("http://192.168.1.2:51783/stream/token/live.ts?x=1&y=2");

        string xml = DidlLiteWriter.CreateVideoItem(
            "Windows <Desktop> & Sound",
            new(uri, "http-get:*:video/mpeg:*"));

        XDocument document = XDocument.Parse(xml);
        XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XElement item = document.Root!.Element(didl + "item")!;
        Assert.Equal("Windows <Desktop> & Sound", item.Element(dc + "title")!.Value);
        Assert.Equal(uri.AbsoluteUri, item.Element(didl + "res")!.Value);
        Assert.Equal("http-get:*:video/mpeg:*", item.Element(didl + "res")!.Attribute("protocolInfo")!.Value);
    }

    [Fact]
    public void CreateAudioItemUsesDlnaAudioClass()
    {
        string xml = DidlLiteWriter.CreateAudioItem(
            "Windows System Audio",
            new(new Uri("http://192.168.1.2/stream/token/live.ts"), "http-get:*:video/mpeg:*"));

        XDocument document = XDocument.Parse(xml);
        XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        XNamespace upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
        XElement item = document.Root!.Element(didl + "item")!;
        Assert.Equal("object.item.audioItem.musicTrack", item.Element(upnp + "class")!.Value);
    }
}
