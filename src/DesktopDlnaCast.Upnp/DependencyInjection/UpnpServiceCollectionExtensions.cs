using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Upnp.Capabilities;
using DesktopDlnaCast.Upnp.Control;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Discovery;
using DesktopDlnaCast.Upnp.Http;
using DesktopDlnaCast.Upnp.Metadata;
using DesktopDlnaCast.Upnp.Rendering;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DesktopDlnaCast.Upnp.DependencyInjection;

public static class UpnpServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopDlnaCastUpnp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<SsdpDiscoveryOptions>()
            .Bind(configuration.GetSection(SsdpDiscoveryOptions.SectionName));
        services.AddSingleton<UpnpHttpTransport>();
        services.AddSingleton<IDeviceDescriptionClient>(provider =>
            new DeviceDescriptionClient(provider.GetRequiredService<UpnpHttpTransport>().Client));
        services.AddSingleton<UpnpSoapClient>(provider =>
            new UpnpSoapClient(provider.GetRequiredService<UpnpHttpTransport>().Client));
        services.AddSingleton<AvTransportClient>();
        services.AddSingleton<ConnectionManagerClient>();
        services.AddSingleton<RenderingControlClient>();
        services.AddSingleton<RendererControlContextResolver>();
        services.AddSingleton<IDlnaRendererClient, DlnaRendererClient>();
        services.AddSingleton<IRendererCapabilityProbe, RendererCapabilityProbe>();
        services.AddSingleton<IStreamMetadataFactory, DidlStreamMetadataFactory>();
        services.AddSingleton<ILanNetworkInterfaceProvider, SystemLanNetworkInterfaceProvider>();
        services.AddSingleton<ISsdpSearchTransport, UdpSsdpSearchTransport>();
        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<SsdpDiscoveryOptions>>().Value);
        services.AddSingleton<IDlnaDiscoveryService, SsdpDiscoveryService>();
        return services;
    }
}
