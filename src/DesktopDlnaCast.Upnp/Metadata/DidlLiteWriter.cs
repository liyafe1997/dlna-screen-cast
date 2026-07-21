using System.Text;
using System.Xml;

namespace DesktopDlnaCast.Upnp.Metadata;

public static class DidlLiteWriter
{
    private const string DidlNamespace = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string UpnpNamespace = "urn:schemas-upnp-org:metadata-1-0/upnp/";

    public static string CreateVideoItem(string title, DidlLiteResource resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(resource);
        if (!resource.Uri.IsAbsoluteUri ||
            (resource.Uri.Scheme != Uri.UriSchemeHttp && resource.Uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The DIDL-Lite resource URI must be an absolute HTTP URI.", nameof(resource));
        }

        if (title.Length > 1024 || string.IsNullOrWhiteSpace(resource.ProtocolInfo) || resource.ProtocolInfo.Length > 4096)
        {
            throw new ArgumentException("The DIDL-Lite metadata exceeds a configured field limit.");
        }

        StringBuilder output = new();
        XmlWriterSettings settings = new()
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
        };
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement(string.Empty, "DIDL-Lite", DidlNamespace);
            writer.WriteAttributeString("xmlns", "dc", null, DublinCoreNamespace);
            writer.WriteAttributeString("xmlns", "upnp", null, UpnpNamespace);
            writer.WriteStartElement("item", DidlNamespace);
            writer.WriteAttributeString("id", "desktop-live");
            writer.WriteAttributeString("parentID", "0");
            writer.WriteAttributeString("restricted", "1");
            writer.WriteElementString("dc", "title", DublinCoreNamespace, title);
            writer.WriteElementString("upnp", "class", UpnpNamespace, "object.item.videoItem");
            writer.WriteStartElement("res", DidlNamespace);
            writer.WriteAttributeString("protocolInfo", resource.ProtocolInfo);
            writer.WriteString(resource.Uri.AbsoluteUri);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return output.ToString();
    }
}

