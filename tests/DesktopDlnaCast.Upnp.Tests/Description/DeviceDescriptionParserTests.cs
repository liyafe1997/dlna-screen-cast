using System.Text;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Services;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Description;

public sealed class DeviceDescriptionParserTests
{
    private static readonly Uri DescriptionUri = new("http://192.168.1.42:1400/root/device.xml");

    [Fact]
    public void ParseReadsRendererAndResolvesRelativeServiceUrls()
    {
        RendererDeviceDescription description = Parse(CreateDescriptionXml());

        Assert.Equal("uuid:test-renderer", description.Udn);
        Assert.Equal("Living Room TV", description.FriendlyName);
        Assert.Equal("DesktopDlnaCast", description.Manufacturer);
        Assert.Equal("MockRenderer", description.ModelName);
        Assert.Equal(3, description.Services.Count);

        UpnpServiceDescription? avTransport = description.FindPreferredService(UpnpServiceType.AvTransportName);
        Assert.NotNull(avTransport);
        Assert.Equal(3, avTransport.ServiceType.Version);
        Assert.Equal(new Uri("http://192.168.1.42:1400/root/upnp/avtransport3/control"), avTransport.ControlUri);
        Assert.Equal("urn:schemas-upnp-org:service:AVTransport:3", avTransport.ServiceType.ToString());
    }

    [Fact]
    public void ParseRejectsDtdAndExternalEntities()
    {
        const string xml = "<!DOCTYPE root [<!ENTITY xxe SYSTEM 'file:///c:/windows/win.ini'>]>" +
            "<root xmlns='urn:schemas-upnp-org:device-1-0'><device>" +
            "<deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>" +
            "<friendlyName>&xxe;</friendlyName><UDN>uuid:test</UDN>" +
            "</device></root>";

        UpnpDescriptionException exception = Assert.Throws<UpnpDescriptionException>(() => Parse(xml));

        Assert.IsType<System.Xml.XmlException>(exception.InnerException);
    }

    [Fact]
    public void ParseRejectsControlUrlOnAnotherHost()
    {
        string xml = CreateDescriptionXml().Replace(
            "upnp/avtransport/control",
            "http://203.0.113.1/control",
            StringComparison.Ordinal);

        Assert.Throws<UpnpDescriptionException>(() => Parse(xml));
    }

    [Fact]
    public void ParseRejectsNonRendererDevice()
    {
        string xml = CreateDescriptionXml().Replace("MediaRenderer", "MediaServer", StringComparison.Ordinal);

        Assert.Throws<UpnpDescriptionException>(() => Parse(xml));
    }

    [Fact]
    public void FindPreferredServiceUsesHighestDeclaredVersion()
    {
        RendererDeviceDescription description = Parse(CreateDescriptionXml());

        UpnpServiceDescription? service = description.FindPreferredService(UpnpServiceType.AvTransportName);

        Assert.NotNull(service);
        Assert.Equal(3, service.ServiceType.Version);
    }

    private static RendererDeviceDescription Parse(string xml)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));
        return DeviceDescriptionParser.Parse(stream, DescriptionUri);
    }

    private static string CreateDescriptionXml() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>1</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>Living Room TV</friendlyName>
            <manufacturer>DesktopDlnaCast</manufacturer>
            <modelName>MockRenderer</modelName>
            <UDN>uuid:test-renderer</UDN>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                <serviceId>urn:upnp-org:serviceId:AVTransport</serviceId>
                <SCPDURL>/upnp/avtransport/scpd.xml</SCPDURL>
                <controlURL>upnp/avtransport/control</controlURL>
                <eventSubURL>/upnp/avtransport/event</eventSubURL>
              </service>
              <service>
                <serviceType>urn:schemas-upnp-org:service:AVTransport:3</serviceType>
                <serviceId>urn:upnp-org:serviceId:AVTransport3</serviceId>
                <controlURL>upnp/avtransport3/control</controlURL>
              </service>
              <service>
                <serviceType>urn:schemas-upnp-org:service:ConnectionManager:2</serviceType>
                <serviceId>urn:upnp-org:serviceId:ConnectionManager</serviceId>
                <controlURL>/upnp/connection/control</controlURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;
}

