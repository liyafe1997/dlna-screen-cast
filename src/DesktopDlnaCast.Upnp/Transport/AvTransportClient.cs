using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Soap;

namespace DesktopDlnaCast.Upnp.Transport;

public sealed class AvTransportClient(UpnpSoapClient soapClient)
{
    public Task SetTransportUriAsync(
        UpnpServiceDescription service,
        Uri streamUri,
        string metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        ArgumentNullException.ThrowIfNull(metadata);
        return soapClient.InvokeAsync(
            service,
            "SetAVTransportURI",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["CurrentURI"] = streamUri.AbsoluteUri,
                ["CurrentURIMetaData"] = metadata,
            },
            cancellationToken);
    }

    public Task PlayAsync(UpnpServiceDescription service, CancellationToken cancellationToken) =>
        soapClient.InvokeAsync(
            service,
            "Play",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["Speed"] = "1",
            },
            cancellationToken);

    public Task StopAsync(UpnpServiceDescription service, CancellationToken cancellationToken) =>
        soapClient.InvokeAsync(
            service,
            "Stop",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
            },
            cancellationToken);

    public async Task<RendererTransportInfo> GetTransportInfoAsync(
        UpnpServiceDescription service,
        CancellationToken cancellationToken)
    {
        string xml = await soapClient.InvokeAsync(
            service,
            "GetTransportInfo",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
            },
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> values = SoapActionResponseParser.Parse(
            xml,
            service.ServiceType,
            "GetTransportInfo");
        if (!values.TryGetValue("CurrentTransportState", out string? state))
        {
            throw new FormatException("GetTransportInfo did not return CurrentTransportState.");
        }

        return new(
            state,
            values.GetValueOrDefault("CurrentTransportStatus"),
            values.GetValueOrDefault("CurrentSpeed"));
    }
}

