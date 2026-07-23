using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Publishing;

public sealed class ContinuousMpegTsPublisherTests
{
    [Fact]
    public async Task GetStreamsFromBufferedStartPointWithoutContentLengthAndStopClosesPort()
    {
        ContinuousMpegTsPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(),
            new LiveStreamPublishOptions(),
            CancellationToken.None);
        byte[] startup = Packet(1);
        await publisher.PublishAsync(
            new(startup, TimeSpan.Zero, StartsAtRandomAccessPoint: true),
            CancellationToken.None);
        using HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        using HttpRequestMessage get = new(HttpMethod.Get, publication.PublicUri);
        using CancellationTokenSource requestCancellation = new(TimeSpan.FromSeconds(5));
        using HttpResponseMessage response = await client.SendAsync(
            get,
            HttpCompletionOption.ResponseHeadersRead,
            requestCancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Null(response.Content.Headers.ContentLength);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        StreamClientRequest request = await publisher.WaitForClientRequestAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        Assert.Equal(HttpMethod.Get.Method, request.Method);

        await using Stream body = await response.Content.ReadAsStreamAsync(requestCancellation.Token);
        byte[] received = new byte[376];
        await body.ReadExactlyAsync(received.AsMemory(0, 188), requestCancellation.Token);
        await publisher.PublishAsync(
            new(Packet(2), TimeSpan.FromMilliseconds(33), StartsAtRandomAccessPoint: false),
            CancellationToken.None);
        await body.ReadExactlyAsync(received.AsMemory(188, 188), requestCancellation.Token);
        Assert.Equal(1, received[0]);
        Assert.Equal(2, received[188]);

        await publisher.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(publication.PublicUri));
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();
    }

    [Fact]
    public async Task GetBeforeRandomAccessPointReturnsServiceUnavailableButHeadIsSupported()
    {
        await using ContinuousMpegTsPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(),
            new LiveStreamPublishOptions(),
            CancellationToken.None);
        using HttpClient client = new();

        using HttpResponseMessage get = await client.GetAsync(publication.PublicUri);
        using HttpRequestMessage headRequest = new(HttpMethod.Head, publication.PublicUri);
        using HttpResponseMessage head = await client.SendAsync(headRequest);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("video/mpeg", head.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task AudioAdtsPublicationUsesMatchingUrlMimeAndProtocolInfo()
    {
        await using ContinuousMpegTsPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(
            CreateRenderer(),
            new LiveStreamPublishOptions(AudioProfile: AudioCastProfile.AacAdts),
            CancellationToken.None);
        using HttpClient client = new();
        using HttpRequestMessage headRequest = new(HttpMethod.Head, publication.PublicUri);
        using HttpResponseMessage head = await client.SendAsync(headRequest);

        Assert.EndsWith("/live.aac", publication.PublicUri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("audio/vnd.dlna.adts", head.Content.Headers.ContentType!.MediaType);
        Assert.Equal(
            "http-get:*:audio/vnd.dlna.adts:DLNA.ORG_PN=AAC_ADTS",
            publication.ProtocolInfo);
        Assert.Equal("Streaming", head.Headers.GetValues("transferMode.dlna.org").Single());
    }

    private static ContinuousMpegTsPublisher CreatePublisher() =>
        new(
            new LoopbackNetworkSelector(),
            Options.Create(new StreamingOptions
            {
                Port = 0,
                AllowLoopbackForTests = true,
                RestrictToRendererAddress = true,
                LiveBufferBytes = 188 * 20,
                LiveBufferDuration = TimeSpan.FromSeconds(5),
                LiveSubscriberQueueChunks = 4,
            }),
            NullLogger<ContinuousMpegTsPublisher>.Instance);

    private static RendererDevice CreateRenderer() =>
        new(
            "uuid:test-renderer",
            "Test Renderer",
            "DesktopDlnaCast",
            "Mock",
            IPAddress.Loopback,
            new("http://127.0.0.1/device.xml"));

    private static byte[] Packet(byte marker)
    {
        byte[] packet = new byte[188];
        packet[0] = marker;
        return packet;
    }

    private sealed class LoopbackNetworkSelector : INetworkInterfaceSelector
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
