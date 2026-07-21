using System.Xml;
using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Discovery;
using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Description;

public static class DeviceDescriptionParser
{
    public const long MaximumCharacters = 1024 * 1024;
    public const int MaximumTextLength = 4096;

    public static RendererDeviceDescription Parse(Stream xml, Uri descriptionUri)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(descriptionUri);
        ValidateBaseUri(descriptionUri);

        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacters,
                MaxCharactersFromEntities = 0,
                CloseInput = false,
            };
            using XmlReader reader = XmlReader.Create(xml, settings);
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            XElement root = document.Root ?? throw new UpnpDescriptionException("The device description has no root element.");
            if (!root.Name.LocalName.Equals("root", StringComparison.Ordinal) ||
                !root.Name.NamespaceName.StartsWith("urn:schemas-upnp-org:device-1-", StringComparison.Ordinal))
            {
                throw new UpnpDescriptionException("The document is not a supported UPnP Device Description.");
            }

            XNamespace ns = root.Name.Namespace;
            XElement? renderer = root
                .Descendants(ns + "device")
                .FirstOrDefault(element => IsMediaRenderer(ReadRequiredValue(element, ns + "deviceType")));
            if (renderer is null)
            {
                throw new UpnpDescriptionException("The device description does not contain a MediaRenderer device.");
            }

            string udn = ReadRequiredValue(renderer, ns + "UDN");
            if (!SsdpUsn.TryGetUdn(udn, out string normalizedUdn))
            {
                throw new UpnpDescriptionException("The renderer UDN is invalid.");
            }

            List<UpnpServiceDescription> services = ParseServices(renderer, ns, descriptionUri);
            return new(
                normalizedUdn,
                ReadRequiredValue(renderer, ns + "friendlyName"),
                ReadOptionalValue(renderer, ns + "manufacturer"),
                ReadOptionalValue(renderer, ns + "modelName"),
                descriptionUri,
                services.AsReadOnly());
        }
        catch (UpnpDescriptionException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new UpnpDescriptionException("The device description XML is invalid or unsafe.", exception);
        }
    }

    private static List<UpnpServiceDescription> ParseServices(
        XElement renderer,
        XNamespace ns,
        Uri descriptionUri)
    {
        List<UpnpServiceDescription> services = [];
        foreach (XElement element in renderer.Elements(ns + "serviceList").Elements(ns + "service"))
        {
            string rawType = ReadRequiredValue(element, ns + "serviceType");
            if (!UpnpServiceType.TryParse(rawType, out UpnpServiceType serviceType))
            {
                continue;
            }

            Uri controlUri = ResolveServiceUri(
                descriptionUri,
                ReadRequiredValue(element, ns + "controlURL"),
                "controlURL");
            Uri? eventUri = ResolveOptionalServiceUri(
                descriptionUri,
                ReadOptionalValue(element, ns + "eventSubURL"),
                "eventSubURL");
            Uri? scpdUri = ResolveOptionalServiceUri(
                descriptionUri,
                ReadOptionalValue(element, ns + "SCPDURL"),
                "SCPDURL");
            services.Add(new(serviceType, controlUri, eventUri, scpdUri));
        }

        return services;
    }

    private static Uri? ResolveOptionalServiceUri(Uri baseUri, string? value, string fieldName) =>
        value is null ? null : ResolveServiceUri(baseUri, value, fieldName);

    private static Uri ResolveServiceUri(Uri baseUri, string value, string fieldName)
    {
        if (!Uri.TryCreate(baseUri, value, out Uri? resolved) ||
            (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(resolved.UserInfo) ||
            !resolved.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpnpDescriptionException($"The renderer {fieldName} is invalid or points to another host.");
        }

        return resolved;
    }

    private static string ReadRequiredValue(XElement parent, XName name) =>
        ReadOptionalValue(parent, name) ??
        throw new UpnpDescriptionException($"The device description is missing {name.LocalName}.");

    private static string? ReadOptionalValue(XElement parent, XName name)
    {
        string? value = parent.Element(name)?.Value.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > MaximumTextLength)
        {
            throw new UpnpDescriptionException($"The device description field {name.LocalName} is too long.");
        }

        return value;
    }

    private static bool IsMediaRenderer(string deviceType) =>
        deviceType.StartsWith("urn:schemas-upnp-org:device:MediaRenderer:", StringComparison.OrdinalIgnoreCase);

    private static void ValidateBaseUri(Uri descriptionUri)
    {
        if (!descriptionUri.IsAbsoluteUri ||
            (descriptionUri.Scheme != Uri.UriSchemeHttp && descriptionUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(descriptionUri.UserInfo))
        {
            throw new ArgumentException("The description URI must be an absolute HTTP URI without credentials.", nameof(descriptionUri));
        }
    }
}

