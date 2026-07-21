using System.Xml;
using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Soap;

public static class SoapActionResponseParser
{
    public const int MaximumCharacters = 256 * 1024;

    public static IReadOnlyDictionary<string, string> Parse(
        string xml,
        UpnpServiceType serviceType,
        string actionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        if (xml.Length > MaximumCharacters)
        {
            throw new FormatException("The SOAP response exceeds the configured size limit.");
        }

        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacters,
                MaxCharactersFromEntities = 0,
            };
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, settings);
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
            XElement body = document.Root?.Element(soap + "Body") ??
                throw new FormatException("The SOAP response has no Body element.");
            XElement action = body.Element(XName.Get($"{actionName}Response", serviceType.ToString())) ??
                throw new FormatException("The SOAP action response element is missing or uses the wrong service type.");
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (XElement element in action.Elements())
            {
                if (element.Value.Length > 64 * 1024 || !values.TryAdd(element.Name.LocalName, element.Value))
                {
                    throw new FormatException("A SOAP response field is duplicated or exceeds the configured size limit.");
                }
            }

            return values;
        }
        catch (XmlException exception)
        {
            throw new FormatException("The SOAP response XML is invalid or unsafe.", exception);
        }
    }
}

