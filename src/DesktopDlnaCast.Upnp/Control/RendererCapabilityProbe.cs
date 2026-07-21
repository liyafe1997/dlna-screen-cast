using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Upnp.Capabilities;

namespace DesktopDlnaCast.Upnp.Control;

internal sealed class RendererCapabilityProbe(
    RendererControlContextResolver contextResolver,
    ConnectionManagerClient connectionManagerClient) : IRendererCapabilityProbe
{
    public async Task<IReadOnlyList<string>> GetSinkProtocolInfoAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        if (context.ConnectionManager is null)
        {
            return [];
        }

        IReadOnlyList<ProtocolInfoEntry> entries = await connectionManagerClient.GetSinkProtocolInfoAsync(
            context.ConnectionManager,
            cancellationToken).ConfigureAwait(false);
        return entries
            .Select(entry => $"{entry.Transport}:{entry.Network}:{entry.ContentFormat}:{entry.AdditionalInfo}")
            .ToArray();
    }
}
