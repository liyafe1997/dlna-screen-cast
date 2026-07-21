using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Upnp.Rendering;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Transport;

namespace DesktopDlnaCast.Upnp.Control;

internal sealed class DlnaRendererClient(
    RendererControlContextResolver contextResolver,
    AvTransportClient transportClient,
    RenderingControlClient renderingControlClient) : IDlnaRendererClient
{
    public async Task SetTransportUriAsync(
        RendererDevice renderer,
        Uri streamUri,
        string metadata,
        CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await transportClient.SetTransportUriAsync(
                context.AvTransport,
                streamUri,
                metadata,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task PlayAsync(RendererDevice renderer, CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await transportClient.PlayAsync(context.AvTransport, cancellationToken).ConfigureAwait(false);
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task StopAsync(RendererDevice renderer, CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await transportClient.StopAsync(context.AvTransport, cancellationToken).ConfigureAwait(false);
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task<string> GetTransportStateAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            RendererTransportInfo info = await transportClient.GetTransportInfoAsync(
                context.AvTransport,
                cancellationToken).ConfigureAwait(false);
            return info.CurrentTransportState;
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task<int?> GetVolumeAsync(RendererDevice renderer, CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        if (context.RenderingControl is null)
        {
            return null;
        }

        try
        {
            return await renderingControlClient.GetVolumeAsync(
                context.RenderingControl,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    public async Task SetVolumeAsync(RendererDevice renderer, int volume, CancellationToken cancellationToken)
    {
        RendererControlContext context = await contextResolver.ResolveAsync(renderer, cancellationToken)
            .ConfigureAwait(false);
        if (context.RenderingControl is null)
        {
            return;
        }

        try
        {
            await renderingControlClient.SetVolumeAsync(
                context.RenderingControl,
                volume,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UpnpSoapException exception)
        {
            throw Translate(exception);
        }
    }

    private static RendererCommandException Translate(UpnpSoapException exception) =>
        new(
            exception.Message,
            exception.StatusCode is null ? null : (int)exception.StatusCode.Value,
            exception.Fault?.UpnpErrorCode,
            exception);
}
