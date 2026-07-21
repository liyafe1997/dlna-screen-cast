using System.Net;
using System.Text;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Http;

namespace DesktopDlnaCast.Upnp.Soap;

public sealed class UpnpSoapClient(HttpClient httpClient)
{
    public const int MaximumResponseBytes = 256 * 1024;

    public async Task<string> InvokeAsync(
        UpnpServiceDescription service,
        string actionName,
        IEnumerable<KeyValuePair<string, string?>> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        string requestXml = SoapEnvelopeWriter.CreateAction(service.ServiceType, actionName, arguments);
        using HttpRequestMessage request = new(HttpMethod.Post, service.ControlUri);
        request.Headers.TryAddWithoutValidation(
            "SOAPAction",
            $"\"{service.ServiceType}#{actionName}\"");
        request.Content = new StringContent(requestXml, Encoding.UTF8, "text/xml");

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        MemoryStream body;
        try
        {
            body = await BoundedHttpContentReader.ReadAsync(
                response.Content,
                MaximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new UpnpSoapException("The SOAP response exceeds the configured size limit.", exception);
        }

        await using (body.ConfigureAwait(false))
        {
            string responseXml = Encoding.UTF8.GetString(body.GetBuffer(), 0, checked((int)body.Length));
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                SoapFault? fault = SoapFaultParser.TryParse(responseXml, out SoapFault? parsedFault)
                    ? parsedFault
                    : null;
                throw new UpnpSoapException(response.StatusCode, fault);
            }

            return responseXml;
        }
    }
}
