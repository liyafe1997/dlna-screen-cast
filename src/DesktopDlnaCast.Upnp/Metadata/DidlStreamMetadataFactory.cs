using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Upnp.Metadata;

public sealed class DidlStreamMetadataFactory : IStreamMetadataFactory
{
    public string CreateVideoItem(string title, StreamPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return DidlLiteWriter.CreateVideoItem(
            title,
            new(publication.PublicUri, publication.ProtocolInfo));
    }

    public string CreateAudioItem(string title, StreamPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return DidlLiteWriter.CreateAudioItem(
            title,
            new(publication.PublicUri, publication.ProtocolInfo));
    }
}
