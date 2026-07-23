using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopDlnaCast.Core.Tests.Casting;

public sealed class StaticMediaTestSessionTests
{
    [Fact]
    public async Task StartAndStopCompleteRequiredStateAndCleanupSequence()
    {
        FakeRendererClient rendererClient = new();
        FakeStreamPublisher publisher = new();
        StaticMediaTestSession session = CreateSession(rendererClient, publisher);
        RendererDevice renderer = CreateRenderer();

        await session.StartAsync(renderer, CancellationToken.None);
        await session.StartAsync(renderer, CancellationToken.None);

        Assert.Equal(CastSessionState.Playing, session.State);
        Assert.Single(rendererClient.SetUriCalls);
        Assert.Equal(1, rendererClient.PlayCount);
        Assert.Equal(1, publisher.StartCount);

        await session.StopAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.Equal(1, rendererClient.StopCount);
        Assert.Equal(1, publisher.StopCount);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task MetadataRejectionRetriesExactlyOnceWithEmptyMetadata()
    {
        FakeRendererClient rendererClient = new() { RejectFirstMetadata = true };
        FakeStreamPublisher publisher = new();
        await using StaticMediaTestSession session = CreateSession(rendererClient, publisher);

        await session.StartAsync(CreateRenderer(), CancellationToken.None);

        Assert.Equal(2, rendererClient.SetUriCalls.Count);
        Assert.Equal("metadata", rendererClient.SetUriCalls[0]);
        Assert.Equal(string.Empty, rendererClient.SetUriCalls[1]);
        Assert.Equal(CastSessionState.Playing, session.State);
    }

    [Fact]
    public async Task PlaybackFailureRecordsStageAndAlwaysCleansLocalPublisher()
    {
        FakeRendererClient rendererClient = new() { PlayFailure = new HttpRequestException("play failed") };
        FakeStreamPublisher publisher = new();
        await using StaticMediaTestSession session = CreateSession(rendererClient, publisher);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            session.StartAsync(CreateRenderer(), CancellationToken.None));

        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.NotNull(session.Failure);
        Assert.Equal(CastFailureStage.PlaybackConfirmation, session.Failure.Stage);
        Assert.Same(rendererClient.PlayFailure, session.Failure.Exception);
        Assert.Equal(1, publisher.StopCount);
        Assert.Equal(1, rendererClient.StopCount);
    }

    [Fact]
    public async Task MissingHttpRequestTimesOutAndCleansUp()
    {
        FakeRendererClient rendererClient = new();
        FakeStreamPublisher publisher = new() { NeverConfirmRequest = true };
        await using StaticMediaTestSession session = CreateSession(
            rendererClient,
            publisher,
            confirmationTimeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.StartAsync(CreateRenderer(), CancellationToken.None));

        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.Equal(CastFailureStage.PlaybackConfirmation, session.Failure!.Stage);
        Assert.Equal(1, publisher.StopCount);
    }

    [Fact]
    public async Task DisposingInactiveSessionDoesNotStopAnotherSessionOwner()
    {
        CastSessionStateMachine stateMachine = new(NullLogger<CastSessionStateMachine>.Instance);
        TransitionToPlaying(stateMachine);
        FakeRendererClient rendererClient = new();
        FakeStreamPublisher publisher = new();
        StaticMediaTestSession session = CreateSession(rendererClient, publisher, stateMachine: stateMachine);

        await session.DisposeAsync();

        Assert.Equal(CastSessionState.Playing, stateMachine.State);
        Assert.Equal(0, rendererClient.StopCount);
        Assert.Equal(0, publisher.StopCount);
    }

    private static StaticMediaTestSession CreateSession(
        FakeRendererClient rendererClient,
        FakeStreamPublisher publisher,
        TimeSpan? confirmationTimeout = null,
        CastSessionStateMachine? stateMachine = null) =>
        new(
            stateMachine ?? new(NullLogger<CastSessionStateMachine>.Instance),
            new FakeCapabilityProbe(),
            publisher,
            new FakeMetadataFactory(),
            rendererClient,
            new()
            {
                PlaybackConfirmationTimeout = confirmationTimeout ?? TimeSpan.FromSeconds(1),
                TransportPollInterval = TimeSpan.FromMilliseconds(10),
                CleanupTimeout = TimeSpan.FromSeconds(1),
            },
            NullLogger<StaticMediaTestSession>.Instance);

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
            "Manufacturer",
            "Model",
            IPAddress.Loopback,
            new("http://127.0.0.1/device.xml"));

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

    private sealed class FakeStreamPublisher : IStreamPublisher
    {
        public bool NeverConfirmRequest { get; init; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<StreamPublication> StartAsync(
            RendererDevice renderer,
            CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.FromResult(new StreamPublication(
                new("http://127.0.0.1/stream/token/test.ts"),
                "http://127.0.0.1/stream/[redacted]/test.ts",
                StreamMode.MpegTsContinuous));
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public async Task<StreamClientRequest> WaitForClientRequestAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (NeverConfirmRequest)
            {
                await Task.Delay(timeout, cancellationToken);
                throw new OperationCanceledException("No renderer HTTP request was observed.", cancellationToken);
            }

            return new(HttpMethod.Get.Method, IPAddress.Loopback, DateTimeOffset.UtcNow);
        }
    }

    private sealed class FakeRendererClient : IDlnaRendererClient
    {
        public bool RejectFirstMetadata { get; init; }

        public Exception? PlayFailure { get; init; }

        public List<string> SetUriCalls { get; } = [];

        public int PlayCount { get; private set; }

        public int StopCount { get; private set; }

        public Task SetTransportUriAsync(
            RendererDevice renderer,
            Uri streamUri,
            string metadata,
            CancellationToken cancellationToken)
        {
            SetUriCalls.Add(metadata);
            if (RejectFirstMetadata && SetUriCalls.Count == 1)
            {
                throw new RendererCommandException("metadata rejected", 500, 714);
            }

            return Task.CompletedTask;
        }

        public Task PlayAsync(RendererDevice renderer, CancellationToken cancellationToken)
        {
            PlayCount++;
            return PlayFailure is null ? Task.CompletedTask : Task.FromException(PlayFailure);
        }

        public Task StopAsync(RendererDevice renderer, CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<int?> GetVolumeAsync(RendererDevice renderer, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);

        public Task SetVolumeAsync(RendererDevice renderer, int volume, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string> GetTransportStateAsync(
            RendererDevice renderer,
            CancellationToken cancellationToken) =>
            Task.FromResult("PLAYING");
    }
}
