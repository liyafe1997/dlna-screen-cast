using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Soap;

namespace DesktopDlnaCast.Upnp.Capabilities;

public sealed class ConnectionManagerClient(UpnpSoapClient soapClient)
{
    public async Task<IReadOnlyList<ProtocolInfoEntry>> GetSinkProtocolInfoAsync(
        UpnpServiceDescription service,
        CancellationToken cancellationToken)
    {
        string xml = await soapClient.InvokeAsync(
            service,
            "GetProtocolInfo",
            [],
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> values = SoapActionResponseParser.Parse(
            xml,
            service.ServiceType,
            "GetProtocolInfo");
        return ProtocolInfoParser.ParseSink(values.GetValueOrDefault("Sink"));
    }
}

