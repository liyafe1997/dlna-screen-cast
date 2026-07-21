using System.Globalization;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Soap;

namespace DesktopDlnaCast.Upnp.Rendering;

public sealed class RenderingControlClient(UpnpSoapClient soapClient)
{
    private const string MasterChannel = "Master";

    public async Task<int> GetVolumeAsync(
        UpnpServiceDescription service,
        CancellationToken cancellationToken)
    {
        string xml = await soapClient.InvokeAsync(
            service,
            "GetVolume",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["Channel"] = MasterChannel,
            },
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> values = SoapActionResponseParser.Parse(
            xml,
            service.ServiceType,
            "GetVolume");
        if (!values.TryGetValue("CurrentVolume", out string? raw) ||
            !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int volume))
        {
            throw new FormatException("GetVolume did not return a numeric CurrentVolume.");
        }

        return volume;
    }

    public Task SetVolumeAsync(
        UpnpServiceDescription service,
        int volume,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volume);
        return soapClient.InvokeAsync(
            service,
            "SetVolume",
            new Dictionary<string, string?>
            {
                ["InstanceID"] = "0",
                ["Channel"] = MasterChannel,
                ["DesiredVolume"] = volume.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }
}
