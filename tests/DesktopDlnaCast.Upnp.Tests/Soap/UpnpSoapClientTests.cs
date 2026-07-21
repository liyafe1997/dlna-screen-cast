using System.Net;
using System.Text;
using System.Xml.Linq;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Soap;

public sealed class UpnpSoapClientTests
{
    private static readonly UpnpServiceDescription AvTransport3 = new(
        new("schemas-upnp-org", "AVTransport", 3),
        new("http://192.168.1.42:1400/avtransport/control"),
        null,
        null);

    [Fact]
    public async Task InvokeAsyncPostsXmlWithExactDeclaredSoapAction()
    {
        string? observedSoapAction = null;
        string? observedBody = null;
        using HttpClient httpClient = new(new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            observedSoapAction = Assert.Single(request.Headers.GetValues("SOAPAction"));
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return CreateResponse(HttpStatusCode.OK, CreateActionResponse("Play", AvTransport3.ServiceType));
        }));
        UpnpSoapClient client = new(httpClient);

        await client.InvokeAsync(
            AvTransport3,
            "Play",
            new Dictionary<string, string?> { ["Speed"] = "1" },
            CancellationToken.None);

        Assert.Equal("\"urn:schemas-upnp-org:service:AVTransport:3#Play\"", observedSoapAction);
        XDocument requestDocument = XDocument.Parse(observedBody!);
        Assert.Equal("1", requestDocument.Descendants().Single(element => element.Name.LocalName == "Speed").Value);
    }

    [Fact]
    public async Task InvokeAsyncThrowsStructuredExceptionForSoapFault()
    {
        const string faultXml =
            "<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault>" +
            "<faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail>" +
            "<UPnPError xmlns='urn:schemas-upnp-org:control-1-0'><errorCode>714</errorCode>" +
            "<errorDescription>Illegal MIME-type</errorDescription></UPnPError>" +
            "</detail></s:Fault></s:Body></s:Envelope>";
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, faultXml))));
        UpnpSoapClient client = new(httpClient);

        UpnpSoapException exception = await Assert.ThrowsAsync<UpnpSoapException>(() =>
            client.InvokeAsync(AvTransport3, "Play", [], CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(714, exception.Fault!.UpnpErrorCode);
        Assert.Equal("Illegal MIME-type", exception.Fault.UpnpErrorDescription);
    }

    [Fact]
    public async Task InvokeAsyncRejectsOversizedResponses()
    {
        byte[] oversized = new byte[UpnpSoapClient.MaximumResponseBytes + 1];
        using HttpClient httpClient = new(new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(oversized),
            })));
        UpnpSoapClient client = new(httpClient);

        UpnpSoapException exception = await Assert.ThrowsAsync<UpnpSoapException>(() =>
            client.InvokeAsync(AvTransport3, "Play", [], CancellationToken.None));

        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    internal static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };

    internal static string CreateActionResponse(string action, UpnpServiceType serviceType, string content = "") =>
        $"<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body>" +
        $"<u:{action}Response xmlns:u='{serviceType}'>{content}</u:{action}Response>" +
        "</s:Body></s:Envelope>";
}

