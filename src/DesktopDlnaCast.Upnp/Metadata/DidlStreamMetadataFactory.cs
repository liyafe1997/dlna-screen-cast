using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Upnp.Metadata;

public sealed class DidlStreamMetadataFactory : IStreamMetadataFactory
{
    public string CreateVideoItem(string title, StreamPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        string protocolInfo = publication.Mode switch
        {
            StreamMode.MpegTsContinuous => "http-get:*:video/mpeg:*",
            StreamMode.Hls => "http-get:*:application/vnd.apple.mpegurl:*",
            _ => "http-get:*:video/mpeg:*",
        };
        return DidlLiteWriter.CreateVideoItem(title, new(publication.PublicUri, protocolInfo));
    }
}

