using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;

namespace DesktopDlnaCast.MockRenderer.Soap;

internal sealed record MockSoapRequest(
    string ServiceType,
    string Action,
    IReadOnlyDictionary<string, string> Arguments);

internal static class MockSoapCodec
{
    private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string ControlNamespace = "urn:schemas-upnp-org:control-1-0";
    private const int MaximumRequestBytes = 256 * 1024;

    public static async Task<MockSoapRequest> ReadRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumRequestBytes)
        {
            throw new InvalidDataException("The SOAP request exceeds the configured size limit.");
        }

        using MemoryStream body = new();
        byte[] buffer = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await request.Body.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > MaximumRequestBytes)
            {
                throw new InvalidDataException("The SOAP request exceeds the configured size limit.");
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        body.Position = 0;
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumRequestBytes,
            MaxCharactersFromEntities = 0,
        };
        using XmlReader reader = XmlReader.Create(body, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XNamespace soap = SoapNamespace;
        XElement soapBody = document.Root?.Element(soap + "Body") ??
            throw new FormatException("The SOAP request does not contain a Body.");
        XElement[] actions = soapBody.Elements().ToArray();
        if (actions.Length != 1)
        {
            throw new FormatException("The SOAP Body must contain exactly one action.");
        }

        XElement action = actions[0];
        Dictionary<string, string> arguments = new(StringComparer.Ordinal);
        foreach (XElement element in action.Elements())
        {
            if (element.Value.Length > 64 * 1024 || !arguments.TryAdd(element.Name.LocalName, element.Value))
            {
                throw new FormatException("A SOAP argument is duplicated or too long.");
            }
        }

        return new(action.Name.NamespaceName, action.Name.LocalName, arguments);
    }

    public static string CreateActionResponse(
        string serviceType,
        string action,
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        StringBuilder output = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = false };
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("s", "Envelope", SoapNamespace);
            writer.WriteStartElement("s", "Body", SoapNamespace);
            writer.WriteStartElement("u", $"{action}Response", serviceType);
            foreach ((string name, string? value) in values)
            {
                writer.WriteElementString(name, value ?? string.Empty);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return output.ToString();
    }

    public static string CreateFault(int errorCode, string description)
    {
        StringBuilder output = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = false };
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("s", "Envelope", SoapNamespace);
            writer.WriteStartElement("s", "Body", SoapNamespace);
            writer.WriteStartElement("s", "Fault", SoapNamespace);
            writer.WriteElementString("faultcode", "s:Client");
            writer.WriteElementString("faultstring", "UPnPError");
            writer.WriteStartElement("detail");
            writer.WriteStartElement("UPnPError", ControlNamespace);
            writer.WriteElementString("errorCode", errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteElementString("errorDescription", description);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return output.ToString();
    }
}
