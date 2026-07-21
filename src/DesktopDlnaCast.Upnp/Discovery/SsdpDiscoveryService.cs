using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Upnp.Description;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed class SsdpDiscoveryService(
    ILanNetworkInterfaceProvider interfaceProvider,
    ISsdpSearchTransport searchTransport,
    IDeviceDescriptionClient descriptionClient,
    SsdpDiscoveryOptions options,
    ILogger<SsdpDiscoveryService> logger) : IDlnaDiscoveryService
{
    private static readonly Action<ILogger, string, string, Exception?> LogSearchFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(100, nameof(LogSearchFailed)),
            "SSDP search failed on interface {InterfaceName} for target {SearchTarget}");

    private static readonly Action<ILogger, string, Exception?> LogInvalidResponse =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(101, nameof(LogInvalidResponse)),
            "Ignored invalid SSDP response: {Reason}");

    private static readonly Action<ILogger, Uri, Exception?> LogDescriptionFailed =
        LoggerMessage.Define<Uri>(
            LogLevel.Warning,
            new EventId(102, nameof(LogDescriptionFailed)),
            "Failed to retrieve or parse renderer description {DescriptionUri}");

    private static readonly Action<ILogger, string, Exception?> LogUdnMismatch =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(103, nameof(LogUdnMismatch)),
            "Ignored renderer whose Device Description UDN does not match SSDP UDN {SsdpUdn}");

    public async IAsyncEnumerable<RendererDevice> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        IReadOnlyList<LanNetworkInterface> interfaces = interfaceProvider.GetEligibleInterfaces();
        List<Task<IReadOnlyList<SsdpDatagram>>> searchTasks = [];
        foreach (LanNetworkInterface networkInterface in interfaces)
        {
            foreach (string searchTarget in options.SearchTargets)
            {
                searchTasks.Add(SearchSafelyAsync(networkInterface, searchTarget, cancellationToken));
            }
        }

        IReadOnlyList<SsdpDatagram>[] batches = await Task.WhenAll(searchTasks).ConfigureAwait(false);
        Dictionary<string, DiscoveredResponse> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (SsdpDatagram datagram in batches.SelectMany(batch => batch))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SsdpResponseParser.TryParse(datagram.Payload, out SsdpResponse? response, out string? error) ||
                response is null)
            {
                LogInvalidResponse(logger, error ?? "Unknown parse error", null);
                continue;
            }

            unique.TryAdd(response.Udn, new(response, datagram.RemoteEndPoint));
        }

        Task<RendererDevice?>[] descriptionTasks = unique.Values
            .Select(discovered => ResolveRendererAsync(discovered, cancellationToken))
            .ToArray();
        RendererDevice?[] devices = await Task.WhenAll(descriptionTasks).ConfigureAwait(false);
        foreach (RendererDevice device in devices
                     .Where(device => device is not null)
                     .Cast<RendererDevice>()
                     .OrderBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(device => device.Udn, StringComparer.OrdinalIgnoreCase))
        {
            yield return device;
        }
    }

    private async Task<IReadOnlyList<SsdpDatagram>> SearchSafelyAsync(
        LanNetworkInterface networkInterface,
        string searchTarget,
        CancellationToken cancellationToken)
    {
        try
        {
            return await searchTransport.SearchAsync(
                networkInterface,
                searchTarget,
                options.MaximumWaitSeconds,
                options.SearchTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            LogSearchFailed(logger, networkInterface.Name, searchTarget, exception);
            return [];
        }
        catch (IOException exception)
        {
            LogSearchFailed(logger, networkInterface.Name, searchTarget, exception);
            return [];
        }
    }

    private async Task<RendererDevice?> ResolveRendererAsync(
        DiscoveredResponse discovered,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.DescriptionTimeout);
        try
        {
            RendererDeviceDescription description = await descriptionClient.GetAsync(
                discovered.Response.Location,
                timeoutSource.Token).ConfigureAwait(false);
            if (!description.Udn.Equals(discovered.Response.Udn, StringComparison.OrdinalIgnoreCase))
            {
                LogUdnMismatch(logger, discovered.Response.Udn, null);
                return null;
            }

            return new(
                description.Udn,
                description.FriendlyName,
                description.Manufacturer,
                description.ModelName,
                discovered.RemoteEndPoint.Address,
                description.DescriptionUri);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDescriptionFailed(logger, discovered.Response.Location, exception);
            return null;
        }
        catch (HttpRequestException exception)
        {
            LogDescriptionFailed(logger, discovered.Response.Location, exception);
            return null;
        }
        catch (FormatException exception)
        {
            LogDescriptionFailed(logger, discovered.Response.Location, exception);
            return null;
        }
        catch (InvalidDataException exception)
        {
            LogDescriptionFailed(logger, discovered.Response.Location, exception);
            return null;
        }
    }

    private static void ValidateOptions(SsdpDiscoveryOptions value)
    {
        if (value.SearchTimeout <= TimeSpan.Zero || value.SearchTimeout > TimeSpan.FromSeconds(30) ||
            value.DescriptionTimeout <= TimeSpan.Zero || value.DescriptionTimeout > TimeSpan.FromMinutes(1) ||
            value.MaximumWaitSeconds is < 1 or > 5 ||
            value.SearchTargets.Count is 0 or > 16)
        {
            throw new InvalidOperationException("The SSDP discovery configuration is invalid.");
        }
    }

    private sealed record DiscoveredResponse(SsdpResponse Response, System.Net.IPEndPoint RemoteEndPoint);
}
