using System.Text;
using System.Xml;
using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Soap;

public static class SoapEnvelopeWriter
{
    private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string SoapEncodingNamespace = "http://schemas.xmlsoap.org/soap/encoding/";
    public const int MaximumArgumentLength = 64 * 1024;

    public static string CreateAction(
        UpnpServiceType serviceType,
        string actionName,
        IEnumerable<KeyValuePair<string, string?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(actionName);
        ArgumentNullException.ThrowIfNull(arguments);
        VerifyElementName(actionName, nameof(actionName));

        StringBuilder output = new();
        XmlWriterSettings settings = new()
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
        };
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("s", "Envelope", SoapNamespace);
            writer.WriteAttributeString("s", "encodingStyle", SoapNamespace, SoapEncodingNamespace);
            writer.WriteStartElement("s", "Body", SoapNamespace);
            writer.WriteStartElement("u", actionName, serviceType.ToString());
            foreach ((string name, string? value) in arguments)
            {
                VerifyElementName(name, nameof(arguments));
                if (value?.Length > MaximumArgumentLength)
                {
                    throw new ArgumentException("A SOAP argument exceeds the configured size limit.", nameof(arguments));
                }

                writer.WriteElementString(name, value ?? string.Empty);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToString();
    }

    private static void VerifyElementName(string value, string parameterName)
    {
        try
        {
            XmlConvert.VerifyNCName(value);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException("The SOAP element name is invalid.", parameterName, exception);
        }
    }
}
