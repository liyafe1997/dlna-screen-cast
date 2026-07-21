using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Description;

public sealed record RendererDeviceDescription(
    string Udn,
    string FriendlyName,
    string? Manufacturer,
    string? ModelName,
    Uri DescriptionUri,
    IReadOnlyList<UpnpServiceDescription> Services)
{
    public UpnpServiceDescription? FindPreferredService(string serviceName) =>
        Services
            .Where(service => service.ServiceType.Name.Equals(serviceName, StringComparison.Ordinal))
            .OrderByDescending(service => service.ServiceType.Version)
            .FirstOrDefault();
}

