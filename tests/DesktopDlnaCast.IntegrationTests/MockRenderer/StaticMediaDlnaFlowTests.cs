using System.Diagnostics;
using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.DependencyInjection;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.MockRenderer;
using DesktopDlnaCast.MockRenderer.Diagnostics;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.DependencyInjection;
using DesktopDlnaCast.Streaming.Publishing;
using DesktopDlnaCast.Upnp.Capabilities;
using DesktopDlnaCast.Upnp.DependencyInjection;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Discovery;
using DesktopDlnaCast.Upnp.Metadata;
using DesktopDlnaCast.Upnp.Services;
using DesktopDlnaCast.Upnp.Soap;
using DesktopDlnaCast.Upnp.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.IntegrationTests.MockRenderer;

public sealed class StaticMediaDlnaFlowTests
{
    [Fact]
    public async Task ProductionSessionOrchestratorCompletesMockRendererFlowWithMetadataFallback()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            RejectMetadata = true,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        RendererDevice renderer = new(
            MockRendererOptions.DefaultUdn,
            "DesktopDlnaCast Mock Renderer",
            "DesktopDlnaCast",
            "MockRenderer",
            IPAddress.Loopback,
            new(mockRenderer.BaseUri!, "device.xml"));
        ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Streaming:Port"] = "0",
            ["Streaming:AllowLoopbackForTests"] = "true",
            ["Streaming:RestrictToRendererAddress"] = "true",
            ["StaticMediaTest:PlaybackConfirmationTimeout"] = "00:00:03",
            ["StaticMediaTest:TransportPollInterval"] = "00:00:00.025",
            ["StaticMediaTest:CleanupTimeout"] = "00:00:02",
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDesktopDlnaCastCore(configuration);
        services.AddDesktopDlnaCastUpnp(configuration);
        services.AddDesktopDlnaCastStreaming(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        IStaticMediaTestSession session = provider.GetRequiredService<IStaticMediaTestSession>();

        await session.StartAsync(renderer, CancellationToken.None);

        Assert.Equal(CastSessionState.Playing, session.State);
        await WaitForEventAsync(mockRenderer.Events, "MediaValidationSucceeded", TimeSpan.FromSeconds(3));
        Assert.Single(mockRenderer.Events.Snapshot(), item => item.Type == "MetadataRejected");

        await session.StopAsync(CancellationToken.None);

        Assert.Equal(CastSessionState.Idle, session.State);
        Assert.Equal("STOPPED", mockRenderer.TransportState);
    }

    [Fact]
    public async Task MockRendererCompletesDiscoveryControlPullValidationAndStopFlow()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            HttpPort = 0,
            SsdpPort = 0,
            RequestMethod = MockRendererRequestMethod.Get,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        Assert.NotNull(mockRenderer.SsdpEndPoint);

        using HttpClient upnpHttpClient = CreateUpnpHttpClient();
        DeviceDescriptionClient descriptionClient = new(upnpHttpClient);
        SsdpDiscoveryService discovery = new(
            new LoopbackInterfaceProvider(),
            new UdpSsdpSearchTransport(mockRenderer.SsdpEndPoint!),
            descriptionClient,
            new()
            {
                SearchTimeout = TimeSpan.FromMilliseconds(250),
                DescriptionTimeout = TimeSpan.FromSeconds(2),
                MaximumWaitSeconds = 1,
            },
            NullLogger<SsdpDiscoveryService>.Instance);

        RendererDevice renderer = Assert.Single(await CollectAsync(
            discovery.DiscoverAsync(CancellationToken.None)));
        Assert.Equal(MockRendererOptions.DefaultUdn, renderer.Udn);
        Assert.Equal(IPAddress.Loopback, renderer.Address);

        RendererDeviceDescription description = await descriptionClient.GetAsync(
            renderer.DescriptionUri,
            CancellationToken.None);
        UpnpServiceDescription avTransport = description.FindPreferredService(UpnpServiceType.AvTransportName)!;
        UpnpServiceDescription connectionManager = description.FindPreferredService(UpnpServiceType.ConnectionManagerName)!;
        UpnpSoapClient soapClient = new(upnpHttpClient);
        ConnectionManagerClient capabilityClient = new(soapClient);
        IReadOnlyList<ProtocolInfoEntry> sinkCapabilities = await capabilityClient.GetSinkProtocolInfoAsync(
            connectionManager,
            CancellationToken.None);
        Assert.Contains(sinkCapabilities, entry => entry.ContentFormat == "video/mpeg");

        await using StaticTestClipPublisher publisher = new(
            new LoopbackNetworkSelector(),
            Options.Create(new StreamingOptions
            {
                Port = 0,
                AllowLoopbackForTests = true,
                RestrictToRendererAddress = true,
            }),
            NullLogger<StaticTestClipPublisher>.Instance);
        StreamPublication publication = await publisher.StartAsync(renderer, CancellationToken.None);
        string metadata = DidlLiteWriter.CreateVideoItem(
            "DLNA Screen Cast Test Pattern",
            new(publication.PublicUri, "http-get:*:video/mpeg:*"));
        AvTransportClient transportClient = new(soapClient);

        await transportClient.SetTransportUriAsync(
            avTransport,
            publication.PublicUri,
            metadata,
            CancellationToken.None);
        await transportClient.PlayAsync(avTransport, CancellationToken.None);

        StreamClientRequest request = await publisher.WaitForClientRequestAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        Assert.Equal(HttpMethod.Get.Method, request.Method);
        RendererTransportInfo playing = await WaitForTransportStateAsync(
            transportClient,
            avTransport,
            "PLAYING",
            TimeSpan.FromSeconds(3));
        Assert.Equal("PLAYING", playing.CurrentTransportState);
        await WaitForEventAsync(mockRenderer.Events, "MediaValidationSucceeded", TimeSpan.FromSeconds(3));

        await transportClient.StopAsync(avTransport, CancellationToken.None);
        RendererTransportInfo stopped = await transportClient.GetTransportInfoAsync(
            avTransport,
            CancellationToken.None);
        Assert.Equal("STOPPED", stopped.CurrentTransportState);
        await publisher.StopAsync(CancellationToken.None);

        IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
        AssertOrdered(
            events,
            "TransportUriSet",
            "PlayStarted",
            "RendererHttpRequestStarted",
            "FirstMediaByteReceived",
            "MediaValidationSucceeded",
            "StopCompleted");
        string token = publication.PublicUri.Segments[^2].TrimEnd('/');
        Assert.DoesNotContain(events.SelectMany(item => item.Data.Values), value =>
            value.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MetadataRejectionAllowsSingleEmptyMetadataFallbackAndCleanup()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            RejectMetadata = true,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        DeviceDescriptionClient descriptionClient = new(httpClient);
        RendererDeviceDescription description = await descriptionClient.GetAsync(
            new(mockRenderer.BaseUri!, "device.xml"),
            CancellationToken.None);
        UpnpServiceDescription avTransport = description.FindPreferredService(UpnpServiceType.AvTransportName)!;
        UpnpSoapClient soapClient = new(httpClient);
        AvTransportClient client = new(soapClient);
        Uri mediaUri = new("http://127.0.0.1:54321/stream/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/test.ts");

        UpnpSoapException rejection = await Assert.ThrowsAsync<UpnpSoapException>(() =>
            client.SetTransportUriAsync(avTransport, mediaUri, "<DIDL-Lite />", CancellationToken.None));
        Assert.Equal(714, rejection.Fault!.UpnpErrorCode);

        await client.SetTransportUriAsync(avTransport, mediaUri, string.Empty, CancellationToken.None);

        IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
        Assert.Single(events, item => item.Type == "MetadataRejected");
        Assert.Single(events, item => item.Type == "TransportUriSet");
    }

    [Fact]
    public async Task HeadPullHonorsDelayRecordsBoundedHeadersAndReachesPlaying()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            RequestMethod = MockRendererRequestMethod.Head,
            PullDelay = TimeSpan.FromMilliseconds(100),
            RequestHeaders = new Dictionary<string, string>
            {
                ["X-Renderer-Test"] = "head-probe",
                ["Authorization"] = "must-not-be-exported",
            },
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) = await CreateTransportClientAsync(
            mockRenderer,
            httpClient);
        await using StaticTestClipPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(CreateRenderer(mockRenderer), CancellationToken.None);

        await client.SetTransportUriAsync(service, publication.PublicUri, string.Empty, CancellationToken.None);
        await client.PlayAsync(service, CancellationToken.None);
        StreamClientRequest request = await publisher.WaitForClientRequestAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        RendererTransportInfo state = await WaitForTransportStateAsync(
            client,
            service,
            "PLAYING",
            TimeSpan.FromSeconds(3));

        Assert.Equal(HttpMethod.Head.Method, request.Method);
        Assert.Equal("PLAYING", state.CurrentTransportState);
        IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
        MockRendererEvent play = Assert.Single(events, item => item.Type == "PlayStarted");
        MockRendererEvent requestStarted = Assert.Single(events, item => item.Type == "RendererHttpRequestStarted");
        Assert.True(requestStarted.Timestamp - play.Timestamp >= TimeSpan.FromMilliseconds(75));
        Assert.Contains(events, item => item.Type == "RendererHttpRequestHeader" &&
            item.Data.GetValueOrDefault("name") == "X-Renderer-Test" &&
            item.Data.GetValueOrDefault("value") == "head-probe");
        Assert.Contains(events, item => item.Type == "RendererHttpRequestHeader" &&
            item.Data.GetValueOrDefault("name") == "Authorization" &&
            item.Data.GetValueOrDefault("value") == "<redacted>");
        Assert.DoesNotContain(events.SelectMany(item => item.Data.Values), value =>
            value.Contains("must-not-be-exported", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InjectedSoapFaultIsReturnedWithMachineReadableUpnpDetails()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            FaultAction = "Play",
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) = await CreateTransportClientAsync(
            mockRenderer,
            httpClient);
        Uri mediaUri = new("http://127.0.0.1:54321/stream/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/test.ts");
        await client.SetTransportUriAsync(service, mediaUri, string.Empty, CancellationToken.None);

        UpnpSoapException exception = await Assert.ThrowsAsync<UpnpSoapException>(() =>
            client.PlayAsync(service, CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(501, exception.Fault!.UpnpErrorCode);
        Assert.Equal("Action Failed", exception.Fault.UpnpErrorDescription);
    }

    [Fact]
    public async Task InjectedMidstreamDisconnectIsObservableAndFailsTruncatedMediaValidation()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            DisconnectAfterBytes = 188,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) = await CreateTransportClientAsync(
            mockRenderer,
            httpClient);
        await using StaticTestClipPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(CreateRenderer(mockRenderer), CancellationToken.None);
        await client.SetTransportUriAsync(service, publication.PublicUri, string.Empty, CancellationToken.None);

        await client.PlayAsync(service, CancellationToken.None);
        await WaitForEventAsync(mockRenderer.Events, "MediaValidationFailed", TimeSpan.FromSeconds(3));

        IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
        Assert.Contains(events, item => item.Type == "RendererHttpCompleted" &&
            item.Data.GetValueOrDefault("reason") == "InjectedDisconnect");
        Assert.Contains(events, item => item.Type == "MediaValidationFailed");
    }

    [Fact]
    public async Task ContinuousPublisherFeedsKeyframeAwareMpegTsToMockRenderer()
    {
        byte[] clip = LoadEmbeddedTestClip();
        await using MockRendererHost mockRenderer = new(new()
        {
            MaximumPullBytes = clip.Length * 3,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) = await CreateTransportClientAsync(
            mockRenderer,
            httpClient);
        await using ContinuousMpegTsPublisher publisher = CreateLivePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(mockRenderer),
            new LiveStreamPublishOptions(),
            CancellationToken.None);
        await publisher.PublishAsync(
            new(clip, TimeSpan.Zero, StartsAtRandomAccessPoint: true),
            CancellationToken.None);
        await client.SetTransportUriAsync(service, publication.PublicUri, string.Empty, CancellationToken.None);

        await client.PlayAsync(service, CancellationToken.None);
        for (int index = 1; index <= 8; index++)
        {
            await Task.Delay(10);
            await publisher.PublishAsync(
                new(clip, TimeSpan.FromSeconds(index), StartsAtRandomAccessPoint: true),
                CancellationToken.None);
        }

        await WaitForEventAsync(mockRenderer.Events, "MediaValidationSucceeded", TimeSpan.FromSeconds(3));
        RendererTransportInfo state = await WaitForTransportStateAsync(
            client,
            service,
            "PLAYING",
            TimeSpan.FromSeconds(3));
        Assert.Equal("PLAYING", state.CurrentTransportState);
        Assert.Contains(mockRenderer.Events.Snapshot(), item => item.Type == "RendererHttpCompleted" &&
            item.Data.GetValueOrDefault("reason") == "ReadLimitReached");

        await client.StopAsync(service, CancellationToken.None);
        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuousPublisherMediaIsValidatedWhenRendererIsStopped()
    {
        byte[] clip = LoadEmbeddedTestClip();
        await using MockRendererHost mockRenderer = new(new()
        {
            MaximumPullBytes = clip.Length * 100,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) = await CreateTransportClientAsync(
            mockRenderer,
            httpClient);
        await using ContinuousMpegTsPublisher publisher = CreateLivePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(mockRenderer),
            new LiveStreamPublishOptions(),
            CancellationToken.None);
        await publisher.PublishAsync(
            new(clip, TimeSpan.Zero, StartsAtRandomAccessPoint: true),
            CancellationToken.None);
        await client.SetTransportUriAsync(service, publication.PublicUri, string.Empty, CancellationToken.None);
        await client.PlayAsync(service, CancellationToken.None);
        await WaitForEventAsync(mockRenderer.Events, "FirstMediaByteReceived", TimeSpan.FromSeconds(3));
        await Task.Delay(100);

        await client.StopAsync(service, CancellationToken.None);

        IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
        Assert.Contains(events, item => item.Type == "RendererHttpCompleted" &&
            item.Data.GetValueOrDefault("reason") == "Canceled");
        Assert.Contains(events, item => item.Type == "MediaValidationSucceeded");
        Assert.DoesNotContain(events, item => item.Type == "MediaValidationFailed");
        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuousPublisherFeedsDeclaredMp3AudioToMockRenderer()
    {
        await using MockRendererHost mockRenderer = new(new()
        {
            MaximumPullBytes = 1024,
            RequireAudio = true,
            RequireVideo = false,
        });
        await mockRenderer.StartAsync(CancellationToken.None);
        using HttpClient httpClient = CreateUpnpHttpClient();
        (AvTransportClient client, UpnpServiceDescription service) =
            await CreateTransportClientAsync(mockRenderer, httpClient);
        await using ContinuousMpegTsPublisher publisher = CreateLivePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(mockRenderer),
            new LiveStreamPublishOptions(AudioProfile: AudioCastProfile.Mp3),
            CancellationToken.None);
        byte[] frame = new byte[256];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        await publisher.PublishAsync(
            new(frame, TimeSpan.Zero, StartsAtRandomAccessPoint: true),
            CancellationToken.None);
        await client.SetTransportUriAsync(
            service,
            publication.PublicUri,
            string.Empty,
            CancellationToken.None);

        await client.PlayAsync(service, CancellationToken.None);
        await WaitForEventAsync(mockRenderer.Events, "FirstMediaByteReceived", TimeSpan.FromSeconds(3));
        for (int index = 1; index < 4; index++)
        {
            await publisher.PublishAsync(
                new(frame, TimeSpan.FromMilliseconds(index * 24), true),
                CancellationToken.None);
        }

        await client.StopAsync(service, CancellationToken.None);
        await WaitForEventAsync(mockRenderer.Events, "MediaValidationSucceeded", TimeSpan.FromSeconds(3));
        Assert.Contains(
            mockRenderer.Events.Snapshot(),
            item => item.Type == "MediaValidationSucceeded" &&
                item.Data.GetValueOrDefault("audioFormat") == "audio/mpeg");

        await publisher.StopAsync(CancellationToken.None);
    }

    private static async Task<(AvTransportClient Client, UpnpServiceDescription Service)>
        CreateTransportClientAsync(MockRendererHost mockRenderer, HttpClient httpClient)
    {
        DeviceDescriptionClient descriptionClient = new(httpClient);
        RendererDeviceDescription description = await descriptionClient.GetAsync(
            new(mockRenderer.BaseUri!, "device.xml"),
            CancellationToken.None);
        UpnpServiceDescription service = description.FindPreferredService(UpnpServiceType.AvTransportName)!;
        return (new(new UpnpSoapClient(httpClient)), service);
    }

    private static StaticTestClipPublisher CreatePublisher() =>
        new(
            new LoopbackNetworkSelector(),
            Options.Create(new StreamingOptions
            {
                Port = 0,
                AllowLoopbackForTests = true,
                RestrictToRendererAddress = true,
            }),
            NullLogger<StaticTestClipPublisher>.Instance);

    private static ContinuousMpegTsPublisher CreateLivePublisher() =>
        new(
            new LoopbackNetworkSelector(),
            Options.Create(new StreamingOptions
            {
                Port = 0,
                AllowLoopbackForTests = true,
                RestrictToRendererAddress = true,
                LiveBufferBytes = 12 * 1024 * 1024,
                LiveBufferDuration = TimeSpan.FromSeconds(5),
                LiveSubscriberQueueChunks = 16,
            }),
            NullLogger<ContinuousMpegTsPublisher>.Instance);

    private static byte[] LoadEmbeddedTestClip()
    {
        using Stream stream = typeof(StaticTestClipPublisher).Assembly.GetManifestResourceStream(
            "DesktopDlnaCast.Streaming.Assets.test-pattern.ts") ??
            throw new InvalidOperationException("The embedded test clip is missing.");
        using MemoryStream output = new();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static RendererDevice CreateRenderer(MockRendererHost mockRenderer) =>
        new(
            MockRendererOptions.DefaultUdn,
            "DesktopDlnaCast Mock Renderer",
            "DesktopDlnaCast",
            "MockRenderer",
            IPAddress.Loopback,
            new(mockRenderer.BaseUri!, "device.xml"));

    private static HttpClient CreateUpnpHttpClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

    private static async Task<IReadOnlyList<RendererDevice>> CollectAsync(
        IAsyncEnumerable<RendererDevice> source)
    {
        List<RendererDevice> devices = [];
        await foreach (RendererDevice device in source)
        {
            devices.Add(device);
        }

        return devices;
    }

    private static async Task<RendererTransportInfo> WaitForTransportStateAsync(
        AvTransportClient client,
        UpnpServiceDescription service,
        string expectedState,
        TimeSpan timeout)
    {
        Stopwatch timeoutWatch = Stopwatch.StartNew();
        while (timeoutWatch.Elapsed < timeout)
        {
            RendererTransportInfo info = await client.GetTransportInfoAsync(service, CancellationToken.None);
            if (info.CurrentTransportState.Equals(expectedState, StringComparison.Ordinal))
            {
                return info;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"MockRenderer did not reach {expectedState} within {timeout}.");
    }

    private static async Task WaitForEventAsync(
        MockRendererEventStore events,
        string eventType,
        TimeSpan timeout)
    {
        Stopwatch timeoutWatch = Stopwatch.StartNew();
        while (timeoutWatch.Elapsed < timeout)
        {
            if (events.Snapshot().Any(item => item.Type == eventType))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"MockRenderer did not record {eventType} within {timeout}.");
    }

    private static void AssertOrdered(
        IReadOnlyList<MockRendererEvent> events,
        params string[] expectedTypes)
    {
        long previousSequence = 0;
        foreach (string expectedType in expectedTypes)
        {
            MockRendererEvent? item = events.FirstOrDefault(entry =>
                entry.Type == expectedType && entry.Sequence > previousSequence);
            Assert.True(
                item is not null,
                $"Expected {expectedType} after sequence {previousSequence}. Actual: " +
                string.Join(", ", events.Select(entry => $"{entry.Sequence}:{entry.Type}")));
            previousSequence = item!.Sequence;
        }
    }

    private sealed class LoopbackInterfaceProvider : ILanNetworkInterfaceProvider
    {
        public IReadOnlyList<LanNetworkInterface> GetEligibleInterfaces() =>
        [
            new("loopback-test", "Loopback Test", IPAddress.Loopback, 1),
        ];
    }

    private sealed class LoopbackNetworkSelector : DesktopDlnaCast.Core.Abstractions.INetworkInterfaceSelector
    {
        public Task<IPAddress> SelectLocalAddressAsync(
            IPAddress rendererAddress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IPAddress.Loopback);
        }
    }
}
