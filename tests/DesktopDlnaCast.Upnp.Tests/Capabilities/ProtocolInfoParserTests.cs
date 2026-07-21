using DesktopDlnaCast.Upnp.Capabilities;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Capabilities;

public sealed class ProtocolInfoParserTests
{
    [Fact]
    public void ParseSinkReadsCommonRendererCapabilities()
    {
        const string value =
            "http-get:*:video/mpeg:DLNA.ORG_PN=MPEG_TS_HD_NA_ISO," +
            " http-get:*:video/mp4:DLNA.ORG_PN=AVC_MP4_MP_HD_720p_AAC";

        IReadOnlyList<ProtocolInfoEntry> entries = ProtocolInfoParser.ParseSink(value);

        Assert.Equal(2, entries.Count);
        Assert.Equal("http-get", entries[0].Transport);
        Assert.Equal("video/mpeg", entries[0].ContentFormat);
        Assert.Contains("MPEG_TS", entries[0].AdditionalInfo, StringComparison.Ordinal);
        Assert.Equal("video/mp4", entries[1].ContentFormat);
    }

    [Fact]
    public void ParseSinkIgnoresMalformedEntriesWithoutDiscardingValidOnes()
    {
        IReadOnlyList<ProtocolInfoEntry> entries = ProtocolInfoParser.ParseSink(
            "malformed,http-get:*:video/mpeg:*");

        ProtocolInfoEntry entry = Assert.Single(entries);
        Assert.Equal("video/mpeg", entry.ContentFormat);
    }
}

