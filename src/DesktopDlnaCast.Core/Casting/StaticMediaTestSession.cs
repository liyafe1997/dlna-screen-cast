using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.Core.Casting;

public sealed class StaticMediaTestSession(
    CastSessionStateMachine stateMachine,
    IRendererCapabilityProbe capabilityProbe,
    IStreamPublisher streamPublisher,
    IStreamMetadataFactory metadataFactory,
    IDlnaRendererClient rendererClient,
    StaticMediaTestOptions options,
    ILogger<StaticMediaTestSession> logger) : IStaticMediaTestSession
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogStarted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(300, nameof(LogStarted)),
            "Static renderer test session {CorrelationId} started for renderer {RendererUdn}");
    private static readonly Action<ILogger, Guid, Exception?> LogMetadataFallback =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(301, nameof(LogMetadataFallback)),
            "Renderer rejected DIDL-Lite metadata in session {CorrelationId}; retrying once with empty metadata");
    private static readonly Action<ILogger, Guid, Exception?> LogFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(302, nameof(LogFailed)),
            "Static renderer test session {CorrelationId} failed");
    private static readonly Action<ILogger, Guid, Exception?> LogCleanupWarning =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(303, nameof(LogCleanupWarning)),
            "A best-effort cleanup operation failed in session {CorrelationId}");
    private static readonly Action<ILogger, Guid, Exception?> LogCompleted =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(304, nameof(LogCompleted)),
            "Static renderer test session {CorrelationId} cleanup completed");

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private RendererDevice? activeRenderer;
    private Guid correlationId;
    private bool ownsSession;
    private bool disposed;

    public CastSessionState State => stateMachine.State;

    public CastFailure? Failure { get; private set; }

    public async Task StartAsync(RendererDevice renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateOptions(options);
            if (State == CastSessionState.Playing && activeRenderer?.Udn.Equals(renderer.Udn, StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            if (State != CastSessionState.Idle)
            {
                throw new InvalidOperationException("Another cast session is already active.");
            }

            correlationId = Guid.NewGuid();
            activeRenderer = renderer;
            ownsSession = true;
            Failure = null;
            LogStarted(logger, correlationId, renderer.Udn, null);
            try
            {
                stateMachine.Transition(CastSessionState.Discovering);
                stateMachine.Transition(CastSessionState.ProbingRenderer);
                await capabilityProbe.GetSinkProtocolInfoAsync(renderer, cancellationToken).ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.StartingMediaPipeline);
                stateMachine.Transition(CastSessionState.WaitingForKeyframe);
                stateMachine.Transition(CastSessionState.Publishing);
                StreamPublication publication = await streamPublisher.StartAsync(renderer, cancellationToken)
                    .ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.SendingTransportUri);
                string metadata = metadataFactory.CreateVideoItem("DLNA Screen Cast Test Pattern", publication);
                await SetTransportUriWithFallbackAsync(renderer, publication, metadata, cancellationToken)
                    .ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.StartingPlayback);
                await rendererClient.PlayAsync(renderer, cancellationToken).ConfigureAwait(false);

                Task<StreamClientRequest> requestConfirmation = streamPublisher.WaitForClientRequestAsync(
                    options.PlaybackConfirmationTimeout,
                    cancellationToken);
                Task transportConfirmation = WaitForPlayingAsync(renderer, cancellationToken);
                await Task.WhenAll(requestConfirmation, transportConfirmation).ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.Playing);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure = CreateFailure(exception);
                stateMachine.TryFault();
                LogFailed(logger, correlationId, exception);
                await CleanupCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CleanupCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        disposed = true;
        lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SetTransportUriWithFallbackAsync(
        RendererDevice renderer,
        StreamPublication publication,
        string metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await rendererClient.SetTransportUriAsync(
                renderer,
                publication.PublicUri,
                metadata,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RendererCommandException exception) when (exception.UpnpErrorCode == 714 && metadata.Length > 0)
        {
            LogMetadataFallback(logger, correlationId, exception);
            await rendererClient.SetTransportUriAsync(
                renderer,
                publication.PublicUri,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForPlayingAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.PlaybackConfirmationTimeout);
        while (true)
        {
            string state = await rendererClient.GetTransportStateAsync(renderer, timeout.Token)
                .ConfigureAwait(false);
            if (state.Equals("PLAYING", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(options.TransportPollInterval, timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task CleanupCoreAsync()
    {
        if (!ownsSession)
        {
            return;
        }

        stateMachine.TryBeginStopping();
        if (activeRenderer is not null)
        {
            using CancellationTokenSource rendererTimeout = new(options.CleanupTimeout);
            try
            {
                await rendererClient.StopAsync(activeRenderer, rendererTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure ??= CreateFailure(exception);
                LogCleanupWarning(logger, correlationId, exception);
            }
        }

        using CancellationTokenSource publisherTimeout = new(options.CleanupTimeout);
        try
        {
            await streamPublisher.StopAsync(publisherTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure ??= CreateFailure(exception);
            LogCleanupWarning(logger, correlationId, exception);
        }
        finally
        {
            activeRenderer = null;
            ownsSession = false;
            if (State == CastSessionState.Stopping)
            {
                stateMachine.Transition(CastSessionState.Idle);
            }

            LogCompleted(logger, correlationId, null);
        }
    }

    private CastFailure CreateFailure(Exception exception)
    {
        CastFailureStage stage = State switch
        {
            CastSessionState.Discovering => CastFailureStage.Discovery,
            CastSessionState.ProbingRenderer => CastFailureStage.RendererProbe,
            CastSessionState.StartingMediaPipeline or CastSessionState.WaitingForKeyframe => CastFailureStage.MediaPipeline,
            CastSessionState.Publishing => CastFailureStage.StreamPublishing,
            CastSessionState.SendingTransportUri => CastFailureStage.TransportControl,
            CastSessionState.StartingPlayback or CastSessionState.Playing => CastFailureStage.PlaybackConfirmation,
            _ => CastFailureStage.Cleanup,
        };
        RendererCommandException? rendererException = exception as RendererCommandException;
        return new(
            stage,
            "The television test did not complete.",
            exception.Message,
            rendererException?.HttpStatus,
            stage is CastFailureStage.TransportControl ? "Retry with empty metadata or the compatibility profile." : null,
            correlationId,
            exception,
            new Dictionary<string, string>
            {
                ["state"] = State.ToString(),
                ["rendererUdn"] = activeRenderer?.Udn ?? string.Empty,
            });
    }

    private static void ValidateOptions(StaticMediaTestOptions value)
    {
        if (value.PlaybackConfirmationTimeout <= TimeSpan.Zero ||
            value.PlaybackConfirmationTimeout > TimeSpan.FromMinutes(1) ||
            value.TransportPollInterval <= TimeSpan.Zero ||
            value.TransportPollInterval > value.PlaybackConfirmationTimeout ||
            value.CleanupTimeout <= TimeSpan.Zero ||
            value.CleanupTimeout > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("The static media test session configuration is invalid.");
        }
    }
}
