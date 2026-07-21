using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Soap;

public sealed class SoapEnvelopeWriterTests
{
    [Fact]
    public void CreateActionUsesRendererDeclaredServiceTypeAndEscapesArguments()
    {
        UpnpServiceType serviceType = new("schemas-upnp-org", "AVTransport", 3);

        string xml = SoapEnvelopeWriter.CreateAction(
            serviceType,
            "SetAVTransportURI",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["CurrentURI"] = "http://192.168.1.2/live.ts?a=1&b=<value>",
                ["CurrentURIMetaData"] = "<DIDL-Lite title=\"A&B\" />",
            });

        XDocument document = XDocument.Parse(xml);
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = serviceType.ToString();
        XElement action = Assert.Single(document.Root!.Element(soap + "Body")!.Elements());
        Assert.Equal(service + "SetAVTransportURI", action.Name);
        Assert.Equal("http://192.168.1.2/live.ts?a=1&b=<value>", action.Element("CurrentURI")!.Value);
        Assert.Equal("<DIDL-Lite title=\"A&B\" />", action.Element("CurrentURIMetaData")!.Value);
    }

    [Fact]
    public void CreateActionRejectsInvalidXmlNames()
    {
        UpnpServiceType serviceType = new("schemas-upnp-org", "AVTransport", 1);

        Assert.Throws<ArgumentException>(() =>
            SoapEnvelopeWriter.CreateAction(serviceType, "Invalid Action", []));
    }
}

