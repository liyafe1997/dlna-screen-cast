using System.Collections.Concurrent;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Services;

namespace DesktopDlnaCast.Upnp.Control;

internal sealed class RendererControlContextResolver(IDeviceDescriptionClient descriptionClient)
{
    private readonly ConcurrentDictionary<string, RendererControlContext> contexts =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<RendererControlContext> ResolveAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (contexts.TryGetValue(renderer.Udn, out RendererControlContext? existing))
        {
            return existing;
        }

        RendererDeviceDescription description = await descriptionClient.GetAsync(
            renderer.DescriptionUri,
            cancellationToken).ConfigureAwait(false);
        if (!description.Udn.Equals(renderer.Udn, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("The renderer Device Description UDN changed after discovery.");
        }

        UpnpServiceDescription avTransport = description.FindPreferredService(UpnpServiceType.AvTransportName) ??
            throw new FormatException("The renderer does not expose an AVTransport service.");
        RendererControlContext created = new(
            description,
            avTransport,
            description.FindPreferredService(UpnpServiceType.ConnectionManagerName),
            description.FindPreferredService(UpnpServiceType.RenderingControlName));
        return contexts.GetOrAdd(renderer.Udn, created);
    }
}

