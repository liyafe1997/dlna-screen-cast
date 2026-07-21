using DesktopDlnaCast.Upnp.Soap;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Soap;

public sealed class SoapFaultParserTests
{
    [Fact]
    public void TryParseReadsUpnpFaultDetails()
    {
        const string xml =
            "<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'>" +
            "<s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring>" +
            "<detail><UPnPError xmlns='urn:schemas-upnp-org:control-1-0'>" +
            "<errorCode>714</errorCode><errorDescription>Illegal MIME-type</errorDescription>" +
            "</UPnPError></detail></s:Fault></s:Body></s:Envelope>";

        Assert.True(SoapFaultParser.TryParse(xml, out SoapFault? fault));
        Assert.NotNull(fault);
        Assert.Equal("s:Client", fault.FaultCode);
        Assert.Equal("UPnPError", fault.FaultString);
        Assert.Equal(714, fault.UpnpErrorCode);
        Assert.Equal("Illegal MIME-type", fault.UpnpErrorDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not-a-fault />")]
    [InlineData("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///c:/windows/win.ini'>]><x>&y;</x>")]
    public void TryParseRejectsMissingMalformedOrUnsafeFaults(string? xml)
    {
        Assert.False(SoapFaultParser.TryParse(xml, out _));
    }
}

