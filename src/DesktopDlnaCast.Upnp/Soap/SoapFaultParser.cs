using System.Xml;
using System.Xml.Linq;

namespace DesktopDlnaCast.Upnp.Soap;

public static class SoapFaultParser
{
    private const int MaximumCharacters = 256 * 1024;

    public static bool TryParse(string? xml, out SoapFault? fault)
    {
        fault = null;
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumCharacters)
        {
            return false;
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
            XElement? element = document
                .Descendants()
                .FirstOrDefault(candidate =>
                    candidate.Name.LocalName.Equals("Fault", StringComparison.Ordinal) &&
                    candidate.Name.NamespaceName.Equals(
                        "http://schemas.xmlsoap.org/soap/envelope/",
                        StringComparison.Ordinal));
            if (element is null)
            {
                return false;
            }

            string? codeText = ReadDescendant(element, "errorCode");
            int? errorCode = int.TryParse(
                codeText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsedCode)
                ? parsedCode
                : null;
            fault = new(
                ReadChild(element, "faultcode"),
                ReadChild(element, "faultstring"),
                errorCode,
                ReadDescendant(element, "errorDescription"));
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string? ReadChild(XElement parent, string localName) =>
        Normalize(parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value);

    private static string? ReadDescendant(XElement parent, string localName) =>
        Normalize(parent.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value);

    private static string? Normalize(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) || value.Length > 4096 ? null : value;
    }
}

