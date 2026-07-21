using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.Core.Casting;

public sealed class LiveCastSession(
    CastSessionStateMachine stateMachine,
    IRendererCapabilityProbe capabilityProbe,
    IMediaCaptureSessionFactory mediaSessionFactory,
    ILiveStreamPublisher streamPublisher,
    IStreamMetadataFactory metadataFactory,
    IDlnaRendererClient rendererClient,
    LiveCastOptions options,
    ILogger<LiveCastSession> logger) : ICastSession
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogStarted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(320, nameof(LogStarted)),
            "Live cast session {CorrelationId} started for renderer {RendererUdn}");
    private static readonly Action<ILogger, Guid, Exception?> LogMetadataFallback =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(321, nameof(LogMetadataFallback)),
            "Renderer rejected live DIDL-Lite metadata in session {CorrelationId}; retrying once without metadata");
    private static readonly Action<ILogger, Guid, Exception?> LogFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(322, nameof(LogFailed)),
            "Live cast session {CorrelationId} failed");
    private static readonly Action<ILogger, Guid, string, Exception?> LogCleanupWarning =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(323, nameof(LogCleanupWarning)),
            "Live cast cleanup operation {Operation} failed in session {CorrelationId}");
    private static readonly Action<ILogger, Guid, Exception?> LogCompleted =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(324, nameof(LogCompleted)),
            "Live cast session {CorrelationId} cleanup completed");
    private static readonly Action<ILogger, Guid, int, Exception?> LogMonitorProbeFailed =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Warning,
            new EventId(325, nameof(LogMonitorProbeFailed)),
            "Transport monitor probe failed {FailureCount} consecutive time(s) in session {CorrelationId}");
    private static readonly Action<ILogger, Guid, CastStopReason, Exception?> LogRendererDisconnected =
        LoggerMessage.Define<Guid, CastStopReason>(
            LogLevel.Information,
            new EventId(326, nameof(LogRendererDisconnected)),
            "Renderer disconnect detected in session {CorrelationId} ({StopReason}); stopping the cast automatically");

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly object cancellationGate = new();
    private RendererDevice? activeRenderer;
    private IMediaCaptureSession? mediaSession;
    private CancellationTokenSource? startupCancellation;
    private CancellationTokenSource? pumpCancellation;
    private Task? pumpTask;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;
    private Guid correlationId;
    private bool ownsSession;
    private int startupInProgress;
    private int stopRequested;
    private bool disposed;

    public CastSessionState State => stateMachine.State;

    public CastFailure? Failure { get; private set; }

    public CastStopReason StopReason { get; private set; }

    public MediaEncoderDiagnostics? EncoderDiagnostics { get; private set; }

    public async Task StartAsync(
        RendererDevice renderer,
        MediaCaptureConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(configuration);
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateOptions(options);
            ValidateConfiguration(configuration);
            if (State != CastSessionState.Idle)
            {
                throw new InvalidOperationException("Another cast session is already active.");
            }

            correlationId = Guid.NewGuid();
            activeRenderer = renderer;
            ownsSession = true;
            Failure = null;
            StopReason = CastStopReason.None;
            EncoderDiagnostics = null;
            CancellationTokenSource newStartupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (cancellationGate)
            {
                startupCancellation = newStartupCancellation;
            }

            CancellationToken startupToken = newStartupCancellation.Token;
            Volatile.Write(ref startupInProgress, 1);
            LogStarted(logger, correlationId, renderer.Udn, null);
            try
            {
                stateMachine.Transition(CastSessionState.Discovering);
                stateMachine.Transition(CastSessionState.ProbingRenderer);
                await capabilityProbe.GetSinkProtocolInfoAsync(renderer, startupToken).ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.StartingMediaPipeline);
                mediaSession = await mediaSessionFactory.CreateAsync(configuration, startupToken)
                    .ConfigureAwait(false);
                await mediaSession.StartAsync(startupToken).ConfigureAwait(false);
                StreamPublication publication = await streamPublisher.StartAsync(
                    renderer,
                    new LiveStreamPublishOptions(configuration.StartAtLiveEdge),
                    startupToken).ConfigureAwait(false);
                pumpCancellation = new CancellationTokenSource();
                pumpTask = PumpMediaAsync(mediaSession, pumpCancellation.Token);
                stateMachine.Transition(CastSessionState.WaitingForKeyframe);
                Task startPoint = streamPublisher.WaitForStartPointAsync(
                    options.StartPointTimeout,
                    startupToken);
                Task first = await Task.WhenAny(startPoint, pumpTask).ConfigureAwait(false);
                if (ReferenceEquals(first, pumpTask))
                {
                    await pumpTask.ConfigureAwait(false);
                    throw new InvalidDataException("The media pipeline ended before publishing a start point.");
                }

                await startPoint.ConfigureAwait(false);
                EncoderDiagnostics = mediaSession.GetEncoderDiagnostics();
                stateMachine.Transition(CastSessionState.Publishing);
                stateMachine.Transition(CastSessionState.SendingTransportUri);
                string metadata = metadataFactory.CreateVideoItem("Windows Desktop", publication);
                await SetTransportUriWithFallbackAsync(renderer, publication, metadata, startupToken)
                    .ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.StartingPlayback);
                await rendererClient.PlayAsync(renderer, startupToken).ConfigureAwait(false);

                Task confirmations = Task.WhenAll(
                    streamPublisher.WaitForClientRequestAsync(
                        options.PlaybackConfirmationTimeout,
                        startupToken),
                    WaitForPlayingAsync(renderer, startupToken));
                Task confirmationWinner = await Task.WhenAny(confirmations, pumpTask).ConfigureAwait(false);
                if (ReferenceEquals(confirmationWinner, pumpTask))
                {
                    await pumpTask.ConfigureAwait(false);
                    throw new InvalidDataException("The media pipeline ended during playback startup.");
                }

                await confirmations.ConfigureAwait(false);
                stateMachine.Transition(CastSessionState.Playing);
                monitorCancellation = new CancellationTokenSource();
                monitorTask = MonitorRendererAsync(renderer, monitorCancellation.Token);
                Volatile.Write(ref startupInProgress, 0);
            }
            catch (OperationCanceledException) when (startupToken.IsCancellationRequested)
            {
                await CleanupCoreAsync().ConfigureAwait(false);
                throw;
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
            Volatile.Write(ref startupInProgress, 0);
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StopReason == CastStopReason.None)
        {
            StopReason = CastStopReason.UserRequested;
        }

        Volatile.Write(ref stopRequested, 1);
        TryCancelStartup();
        bool lockTaken = false;
        try
        {
            await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            await CleanupCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                lifecycle.Release();
            }

            Volatile.Write(ref stopRequested, 0);
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

    private async Task PumpMediaAsync(IMediaCaptureSession source, CancellationToken cancellationToken)
    {
        await foreach (MediaStreamChunk chunk in source.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await streamPublisher.PublishAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
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

    private async Task MonitorRendererAsync(RendererDevice renderer, CancellationToken cancellationToken)
    {
        int stoppedObservations = 0;
        int failedObservations = 0;
        try
        {
            while (true)
            {
                await Task.Delay(options.TransportMonitorInterval, cancellationToken).ConfigureAwait(false);
                string state;
                try
                {
                    using CancellationTokenSource callTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    callTimeout.CancelAfter(options.TransportMonitorCallTimeout);
                    state = await rendererClient.GetTransportStateAsync(renderer, callTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
                {
                    stoppedObservations = 0;
                    failedObservations++;
                    LogMonitorProbeFailed(logger, correlationId, failedObservations, exception);
                    if (failedObservations >= options.TransportMonitorFailureThreshold)
                    {
                        BeginAutoStop(CastStopReason.RendererUnreachable);
                        return;
                    }

                    continue;
                }

                failedObservations = 0;
                if (state.Equals("STOPPED", StringComparison.OrdinalIgnoreCase) ||
                    state.Equals("NO_MEDIA_PRESENT", StringComparison.OrdinalIgnoreCase))
                {
                    stoppedObservations++;
                    if (stoppedObservations >= options.TransportMonitorStoppedThreshold)
                    {
                        BeginAutoStop(CastStopReason.RendererReportedStopped);
                        return;
                    }
                }
                else
                {
                    stoppedObservations = 0;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void BeginAutoStop(CastStopReason reason)
    {
        StopReason = reason;
        LogRendererDisconnected(logger, correlationId, reason, null);
        _ = Task.Run(async () =>
        {
            try
            {
                await lifecycle.WaitAsync().ConfigureAwait(false);
                try
                {
                    await CleanupCoreAsync().ConfigureAwait(false);
                }
                finally
                {
                    lifecycle.Release();
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogCleanupWarning(logger, correlationId, "AutoStop", exception);
            }
        });
    }

    private async Task WaitForPlayingAsync(RendererDevice renderer, CancellationToken cancellationToken)
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
        monitorCancellation?.Cancel();
        if (monitorTask is not null)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogCleanupWarning(logger, correlationId, "TransportMonitor", exception);
            }
        }

        monitorCancellation?.Dispose();
        monitorCancellation = null;
        monitorTask = null;
        // Sending Stop to a renderer that is already unreachable only delays cleanup until the timeout.
        await TryCleanupAsync(
            "RendererStop",
            token => activeRenderer is null || StopReason == CastStopReason.RendererUnreachable
                ? Task.CompletedTask
                : rendererClient.StopAsync(activeRenderer, token))
            .ConfigureAwait(false);

        streamPublisher.Complete();
        pumpCancellation?.Cancel();
        if (pumpTask is not null)
        {
            try
            {
                await pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pumpCancellation?.IsCancellationRequested == true)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure ??= CreateFailure(exception);
                LogCleanupWarning(logger, correlationId, "MediaPump", exception);
            }
        }

        await TryCleanupAsync("HttpPublisher", streamPublisher.StopAsync).ConfigureAwait(false);
        if (mediaSession is not null)
        {
            await TryCleanupAsync("MediaSessionStop", mediaSession.StopAsync).ConfigureAwait(false);
            try
            {
                await mediaSession.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure ??= CreateFailure(exception);
                LogCleanupWarning(logger, correlationId, "MediaSessionDispose", exception);
            }
        }

        pumpCancellation?.Dispose();
        CancellationTokenSource? cancellationToDispose;
        lock (cancellationGate)
        {
            cancellationToDispose = startupCancellation;
            startupCancellation = null;
        }

        cancellationToDispose?.Dispose();
        pumpCancellation = null;
        pumpTask = null;
        mediaSession = null;
        activeRenderer = null;
        ownsSession = false;
        if (State == CastSessionState.Stopping)
        {
            stateMachine.Transition(CastSessionState.Idle);
        }

        LogCompleted(logger, correlationId, null);
    }

    private void TryCancelStartup()
    {
        if (Volatile.Read(ref startupInProgress) == 0 || Volatile.Read(ref stopRequested) == 0)
        {
            return;
        }

        lock (cancellationGate)
        {
            startupCancellation?.Cancel();
        }
    }

    private async Task TryCleanupAsync(string operation, Func<CancellationToken, Task> action)
    {
        using CancellationTokenSource timeout = new(options.CleanupTimeout);
        try
        {
            await action(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure ??= CreateFailure(exception);
            LogCleanupWarning(logger, correlationId, operation, exception);
        }
    }

    private CastFailure CreateFailure(Exception exception)
    {
        CastFailureStage stage = State switch
        {
            CastSessionState.Discovering => CastFailureStage.Discovery,
            CastSessionState.ProbingRenderer => CastFailureStage.RendererProbe,
            CastSessionState.StartingMediaPipeline or CastSessionState.WaitingForKeyframe =>
                CastFailureStage.MediaPipeline,
            CastSessionState.Publishing => CastFailureStage.StreamPublishing,
            CastSessionState.SendingTransportUri => CastFailureStage.TransportControl,
            CastSessionState.StartingPlayback or CastSessionState.Playing =>
                CastFailureStage.PlaybackConfirmation,
            _ => CastFailureStage.Cleanup,
        };
        RendererCommandException? rendererException = exception as RendererCommandException;
        return new(
            stage,
            "Desktop streaming did not complete.",
            exception.Message,
            rendererException?.HttpStatus,
            stage is CastFailureStage.TransportControl ? "Retry with a compatibility profile." : null,
            correlationId,
            exception,
            new Dictionary<string, string>
            {
                ["state"] = State.ToString(),
                ["rendererUdn"] = activeRenderer?.Udn ?? string.Empty,
            });
    }

    private static void ValidateConfiguration(MediaCaptureConfiguration value)
    {
        if (value.SourceHandle == 0 ||
            value.Width is < 2 or > 7680 ||
            value.Height is < 2 or > 4320 ||
            (value.Width & 1) != 0 ||
            (value.Height & 1) != 0 ||
            value.FrameRate is < 1 or > 30 ||
            value.VideoBitrate is < 100_000 or > 100_000_000 ||
            value.GopFrames is < 1 or > 300 ||
            value.AudioBitrate is < 32_000 or > 512_000 ||
            (value.MuteLocalPlayback && !value.IncludeAudio))
        {
            throw new ArgumentException("The media capture configuration is invalid.", nameof(value));
        }
    }

    private static void ValidateOptions(LiveCastOptions value)
    {
        if (value.StartPointTimeout <= TimeSpan.Zero ||
            value.StartPointTimeout > TimeSpan.FromMinutes(1) ||
            value.PlaybackConfirmationTimeout <= TimeSpan.Zero ||
            value.PlaybackConfirmationTimeout > TimeSpan.FromMinutes(1) ||
            value.TransportPollInterval <= TimeSpan.Zero ||
            value.TransportPollInterval > value.PlaybackConfirmationTimeout ||
            value.CleanupTimeout <= TimeSpan.Zero ||
            value.CleanupTimeout > TimeSpan.FromMinutes(1) ||
            value.TransportMonitorInterval <= TimeSpan.Zero ||
            value.TransportMonitorInterval > TimeSpan.FromMinutes(1) ||
            value.TransportMonitorCallTimeout <= TimeSpan.Zero ||
            value.TransportMonitorCallTimeout > TimeSpan.FromMinutes(1) ||
            value.TransportMonitorStoppedThreshold is < 1 or > 10 ||
            value.TransportMonitorFailureThreshold is < 1 or > 10)
        {
            throw new InvalidOperationException("The live cast session configuration is invalid.");
        }
    }
}
