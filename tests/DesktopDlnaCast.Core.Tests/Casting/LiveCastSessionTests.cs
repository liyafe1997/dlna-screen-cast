using System.Net;
using System.Runtime.CompilerServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopDlnaCast.Core.Tests.Casting;

public sealed class LiveCastSessionTests
{
    [Fact]
    public async Task StartWaitsForRandomAccessPointHttpRequestAndPlayingThenStopsInOrder()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        await session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);

        Assert.Equal(CastSessionState.Playing, session.State);
        Assert.Equal(FakeMediaSession.Diagnostics, session.EncoderDiagnostics);
        Assert.True(publisher.RandomAccessPointPublished);
        Assert.Equal(new LiveStreamPublishOptions(StartAtLiveEdge: false), publisher.PublishOptions);
        AssertOrdered(
            events,
            "MediaStart",
            "PublisherStart",
            "PublishStartPoint",
            "EncoderDiagnostics",
            "SetUri",
            "Play");

        await session.StopAsync(CancellationToken.None);

        Assert.Equal(CastSessionState.Idle, session.State);
        AssertOrdered(events, "RendererStop", "PublisherComplete", "PublisherStop", "MediaStop", "MediaDispose");
        Assert.Equal(1, media.StopCount);
        Assert.Equal(1, media.DisposeCount);
    }

    [Fact]
    public async Task StartForwardsLiveEdgePreferenceToThePublisher()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        await session.StartAsync(
            CreateRenderer(),
            CreateConfiguration() with { StartAtLiveEdge = true },
            CancellationToken.None);

        Assert.Equal(new LiveStreamPublishOptions(StartAtLiveEdge: true), publisher.PublishOptions);
        await session.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PipelineEndingBeforeStartPointFailsAndCleansEveryOwnedResource()
    {
        List<string> events = [];
        FakeMediaSession media = new(events) { EndBeforeStartPoint = true };
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None));

        Assert.Contains("start point", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.Equal(CastFailureStage.MediaPipeline, session.Failure!.Stage);
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, media.StopCount);
        Assert.Equal(1, media.DisposeCount);
    }

    [Fact]
    public async Task MetadataFault714RetriesOnceWithoutMetadata()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events) { RejectFirstMetadata = true };
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        await session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);

        Assert.Equal(2, rendererClient.Metadata.Count);
        Assert.Equal("metadata", rendererClient.Metadata[0]);
        Assert.Equal(string.Empty, rendererClient.Metadata[1]);
    }

    [Fact]
    public async Task StopCancelsStartupWhileWaitingForRandomAccessPoint()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events) { SuppressStartPoint = true };
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        Task start = session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);
        await publisher.FirstChunkPublished.WaitAsync(TimeSpan.FromSeconds(1));

        Task stop = session.StopAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await stop.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.Null(session.Failure);
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, media.StopCount);
        Assert.Equal(1, media.DisposeCount);
    }

    [Fact]
    public async Task StartupCancellationTokenDoesNotOwnPlayingSessionLifetime()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        using CancellationTokenSource startupCancellation = new();
        await session.StartAsync(CreateRenderer(), CreateConfiguration(), startupCancellation.Token);

        startupCancellation.Cancel();
        await Task.Delay(50);

        Assert.Equal(CastSessionState.Playing, session.State);
        Assert.DoesNotContain("PublisherComplete", events);
        await session.StopAsync(CancellationToken.None);
        Assert.Equal(CastSessionState.Idle, session.State);
    }

    [Fact]
    public async Task RejectsOddOutputDimensionsBeforeAllocatingNativeSession()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        MediaCaptureConfiguration invalid = CreateConfiguration() with { Width = 1279 };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.StartAsync(CreateRenderer(), invalid, CancellationToken.None));

        Assert.Empty(events);
    }

    [Fact]
    public async Task RejectsFrameRatesAboveMilestoneTwoLimitBeforeAllocatingNativeSession()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        MediaCaptureConfiguration invalid = CreateConfiguration() with { FrameRate = 60 };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.StartAsync(CreateRenderer(), invalid, CancellationToken.None));

        Assert.Empty(events);
    }

    [Fact]
    public async Task RejectsLocalMuteWhenSystemAudioIsDisabled()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        MediaCaptureConfiguration invalid = CreateConfiguration() with
        {
            IncludeAudio = false,
            MuteLocalPlayback = true,
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.StartAsync(CreateRenderer(), invalid, CancellationToken.None));

        Assert.Empty(events);
    }

    [Fact]
    public async Task RejectsAudioOnlyWhenSystemAudioIsDisabled()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        MediaCaptureConfiguration invalid = CreateConfiguration() with
        {
            IncludeAudio = false,
            AudioOnly = true,
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.StartAsync(CreateRenderer(), invalid, CancellationToken.None));

        Assert.Empty(events);
    }

    [Fact]
    public async Task AudioOnlyUsesAudioMetadata()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        await session.StartAsync(
            CreateRenderer(),
            CreateConfiguration() with { AudioOnly = true },
            CancellationToken.None);

        Assert.Equal("audio-metadata", rendererClient.Metadata.Single());
    }

    [Fact]
    public async Task AudioOnlyRetriesNextFiniteProfileAfterPlaybackFailure()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events) { PlayFailuresRemaining = 1 };
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);

        await session.StartAsync(
            CreateRenderer(),
            CreateConfiguration() with { AudioOnly = true },
            CancellationToken.None);

        Assert.Equal(CastSessionState.Playing, session.State);
        Assert.Equal(
            [AudioCastProfile.Mp3, AudioCastProfile.AacAdts],
            publisher.PublishedOptions.Select(option => option.AudioProfile));
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, media.StopCount);
        Assert.Contains("RendererStop", events);
    }

    [Fact]
    public async Task RendererReportingStoppedTriggersAutomaticStopAndCleanup()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        await session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);

        rendererClient.TransportState = static () => "STOPPED";
        await WaitForAsync(() => session.State == CastSessionState.Idle);

        Assert.Equal(CastStopReason.RendererReportedStopped, session.StopReason);
        Assert.Null(session.Failure);
        Assert.Contains("RendererStop", events);
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, media.StopCount);
        Assert.Equal(1, media.DisposeCount);
    }

    [Fact]
    public async Task RendererProbeFailuresTriggerAutomaticStopWithoutSendingRendererStop()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        await session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);

        rendererClient.TransportState = static () => throw new HttpRequestException("unreachable");
        await WaitForAsync(() => session.State == CastSessionState.Idle);

        Assert.Equal(CastStopReason.RendererUnreachable, session.StopReason);
        Assert.DoesNotContain("RendererStop", events);
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, media.StopCount);
        Assert.Equal(1, media.DisposeCount);
    }

    [Fact]
    public async Task UserStopReportsUserRequestedStopReason()
    {
        List<string> events = [];
        FakeMediaSession media = new(events);
        FakeLivePublisher publisher = new(events);
        FakeRendererClient rendererClient = new(events);
        await using LiveCastSession session = CreateSession(media, publisher, rendererClient);
        await session.StartAsync(CreateRenderer(), CreateConfiguration(), CancellationToken.None);

        await session.StopAsync(CancellationToken.None);

        Assert.Equal(CastStopReason.UserRequested, session.StopReason);
        Assert.Equal(CastSessionState.Idle, session.State);
    }

    [Fact]
    public async Task DisposingInactiveLiveSessionDoesNotStopAnotherSessionOwner()
    {
        List<string> events = [];
        CastSessionStateMachine stateMachine = new(NullLogger<CastSessionStateMachine>.Instance);
        TransitionToPlaying(stateMachine);
        LiveCastSession session = CreateSession(
            new(events),
            new(events),
            new(events),
            stateMachine);

        await session.DisposeAsync();

        Assert.Equal(CastSessionState.Playing, stateMachine.State);
        Assert.Empty(events);
    }

    private static LiveCastSession CreateSession(
        FakeMediaSession media,
        FakeLivePublisher publisher,
        FakeRendererClient rendererClient,
        CastSessionStateMachine? stateMachine = null)
    {
        stateMachine ??= new(NullLogger<CastSessionStateMachine>.Instance);
        return new(
            stateMachine,
            new FakeCapabilityProbe(),
            new FakeMediaSessionFactory(media),
            publisher,
            new FakeMetadataFactory(),
            rendererClient,
            new()
            {
                StartPointTimeout = TimeSpan.FromSeconds(1),
                PlaybackConfirmationTimeout = TimeSpan.FromSeconds(1),
                TransportPollInterval = TimeSpan.FromMilliseconds(10),
                CleanupTimeout = TimeSpan.FromSeconds(1),
                TransportMonitorInterval = TimeSpan.FromMilliseconds(20),
                TransportMonitorCallTimeout = TimeSpan.FromMilliseconds(500),
                TransportMonitorStoppedThreshold = 2,
                TransportMonitorFailureThreshold = 3,
            },
            NullLogger<LiveCastSession>.Instance);
    }

    private static void TransitionToPlaying(CastSessionStateMachine stateMachine)
    {
        stateMachine.Transition(CastSessionState.Discovering);
        stateMachine.Transition(CastSessionState.ProbingRenderer);
        stateMachine.Transition(CastSessionState.StartingMediaPipeline);
        stateMachine.Transition(CastSessionState.WaitingForKeyframe);
        stateMachine.Transition(CastSessionState.Publishing);
        stateMachine.Transition(CastSessionState.SendingTransportUri);
        stateMachine.Transition(CastSessionState.StartingPlayback);
        stateMachine.Transition(CastSessionState.Playing);
    }

    private static RendererDevice CreateRenderer() =>
        new(
            "uuid:renderer",
            "Renderer",
            "DesktopDlnaCast",
            "Mock",
            IPAddress.Loopback,
            new("http://127.0.0.1/device.xml"));

    private static MediaCaptureConfiguration CreateConfiguration() =>
        new(
            CaptureSourceKind.Display,
            SourceHandle: 1,
            IncludeCursor: true,
            Width: 1280,
            Height: 720,
            FrameRate: 30,
            VideoBitrate: 3_000_000,
            GopFrames: 30,
            IncludeAudio: true,
            AudioBitrate: 128_000);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the expected session state.");
    }

    private static void AssertOrdered(IReadOnlyList<string> events, params string[] expected)
    {
        int previous = -1;
        foreach (string item in expected)
        {
            int index = events.IndexOf(item, previous + 1);
            Assert.True(index > previous, $"Expected {item} after index {previous}: {string.Join(", ", events)}");
            previous = index;
        }
    }

    private sealed class FakeMediaSession(List<string> events) : IMediaCaptureSession
    {
        public static MediaEncoderDiagnostics Diagnostics { get; } = new(
            "Test H.264 Encoder",
            IsHardware: false,
            AcceptedWidth: 1280,
            AcceptedHeight: 720,
            FrameRateNumerator: 30,
            FrameRateDenominator: 1,
            AcceptedVideoBitrate: 3_000_000,
            H264Profile: 77,
            AcceptedGopFrames: 30,
            AcceptedBFrameCount: 0,
            VideoProcessorBackend: MediaVideoProcessorBackend.Libswscale,
            AudioEnabled: true,
            AcceptedAudioBitrate: 128_000,
            AudioSampleRate: 48_000,
            AudioChannels: 2);

        public bool EndBeforeStartPoint { get; init; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Add("MediaStart");
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<MediaStreamChunk> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (EndBeforeStartPoint)
            {
                yield break;
            }

            yield return new(new byte[188], TimeSpan.Zero, StartsAtRandomAccessPoint: true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public MediaSessionStatistics GetStatistics() =>
            new(1, 1, 0, 1, 1, 0, 0, 0, TimeSpan.Zero, 0, 0);

        public MediaEncoderDiagnostics GetEncoderDiagnostics()
        {
            events.Add("EncoderDiagnostics");
            return Diagnostics;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            events.Add("MediaStop");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            events.Add("MediaDispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeMediaSessionFactory(FakeMediaSession session) : IMediaCaptureSessionFactory
    {
        public Task<IMediaCaptureSession> CreateAsync(
            MediaCaptureConfiguration configuration,
            CancellationToken cancellationToken) => Task.FromResult<IMediaCaptureSession>(session);
    }

    private sealed class FakeLivePublisher(List<string> events) : ILiveStreamPublisher
    {
        private readonly TaskCompletionSource startPoint = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource firstChunkPublished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SuppressStartPoint { get; init; }

        public Task FirstChunkPublished => firstChunkPublished.Task;

        public bool RandomAccessPointPublished { get; private set; }

        public int StopCount { get; private set; }

        public LiveStreamPublishOptions? PublishOptions { get; private set; }

        public List<LiveStreamPublishOptions> PublishedOptions { get; } = [];

        public Task<StreamPublication> StartAsync(
            RendererDevice renderer,
            LiveStreamPublishOptions publishOptions,
            CancellationToken cancellationToken)
        {
            PublishOptions = publishOptions;
            PublishedOptions.Add(publishOptions);
            events.Add("PublisherStart");
            return Task.FromResult(new StreamPublication(
                new("http://127.0.0.1/stream/redacted/live.ts"),
                "http://127.0.0.1/stream/re...ed/live.ts",
                StreamMode.MpegTsContinuous));
        }

        public ValueTask PublishAsync(MediaStreamChunk chunk, CancellationToken cancellationToken)
        {
            firstChunkPublished.TrySetResult();
            if (chunk.StartsAtRandomAccessPoint)
            {
                RandomAccessPointPublished = true;
                events.Add("PublishStartPoint");
                if (!SuppressStartPoint)
                {
                    startPoint.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        }

        public Task WaitForStartPointAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            startPoint.Task.WaitAsync(cancellationToken);

        public void Complete(Exception? completionException = null) => events.Add("PublisherComplete");

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            events.Add("PublisherStop");
            return Task.CompletedTask;
        }

        public Task<StreamClientRequest> WaitForClientRequestAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(new StreamClientRequest(
                HttpMethod.Get.Method,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRendererClient(List<string> events) : IDlnaRendererClient
    {
        private volatile Func<string> transportState = static () => "PLAYING";

        public bool RejectFirstMetadata { get; init; }

        public int PlayFailuresRemaining { get; set; }

        public List<string> Metadata { get; } = [];

        public Func<string> TransportState
        {
            set => transportState = value;
        }

        public Task SetTransportUriAsync(
            RendererDevice renderer,
            Uri streamUri,
            string metadata,
            CancellationToken cancellationToken)
        {
            Metadata.Add(metadata);
            events.Add("SetUri");
            if (RejectFirstMetadata && Metadata.Count == 1)
            {
                throw new RendererCommandException("rejected", 500, 714);
            }

            return Task.CompletedTask;
        }

        public Task PlayAsync(RendererDevice renderer, CancellationToken cancellationToken)
        {
            events.Add("Play");
            if (PlayFailuresRemaining > 0)
            {
                PlayFailuresRemaining--;
                throw new RendererCommandException("unsupported profile", 500, 716);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(RendererDevice renderer, CancellationToken cancellationToken)
        {
            events.Add("RendererStop");
            return Task.CompletedTask;
        }

        public Task<string> GetTransportStateAsync(
            RendererDevice renderer,
            CancellationToken cancellationToken) => Task.FromResult(transportState());

        public Task<int?> GetVolumeAsync(RendererDevice renderer, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);

        public Task SetVolumeAsync(RendererDevice renderer, int volume, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeCapabilityProbe : IRendererCapabilityProbe
    {
        public Task<IReadOnlyList<string>> GetSinkProtocolInfoAsync(
            RendererDevice renderer,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["http-get:*:video/mpeg:*"]);
    }

    private sealed class FakeMetadataFactory : IStreamMetadataFactory
    {
        public string CreateVideoItem(string title, StreamPublication publication) => "metadata";

        public string CreateAudioItem(string title, StreamPublication publication) => "audio-metadata";
    }
}

internal static class ListSearchExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value, int startIndex)
    {
        for (int index = startIndex; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
