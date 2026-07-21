using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Publishing;
using DesktopDlnaCast.Streaming.Security;
using DesktopDlnaCast.Streaming.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Publishing;

public sealed class StaticTestClipPublisherTests
{
    [Fact]
    public async Task StartAsyncServesHeadAndGetWithExpectedMediaHeadersAndBytes()
    {
        await using StaticTestClipPublisher publisher = CreatePublisher();
        RendererDevice renderer = CreateRenderer();

        StreamPublication publication = await publisher.StartAsync(renderer, CancellationToken.None);
        StreamPublication repeated = await publisher.StartAsync(renderer, CancellationToken.None);

        Assert.Equal(publication, repeated);
        Assert.Equal(StreamMode.MpegTsContinuous, publication.Mode);
        Assert.DoesNotContain(
            publication.PublicUri.Segments[^2].TrimEnd('/'),
            publication.RedactedUri,
            StringComparison.Ordinal);
        using HttpClient client = new();
        using HttpRequestMessage headRequest = new(HttpMethod.Head, publication.PublicUri);
        using HttpResponseMessage headResponse = await client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal("video/mpeg", headResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal(289332, headResponse.Content.Headers.ContentLength);
        Assert.Contains("no-store", headResponse.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        StreamClientRequest observation = await publisher.WaitForClientRequestAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        Assert.Equal(HttpMethod.Head.Method, observation.Method);
        Assert.Equal(IPAddress.Loopback, observation.RemoteAddress);

        byte[] media = await client.GetByteArrayAsync(publication.PublicUri);
        Assert.Equal(289332, media.Length);
        Assert.Equal(0, media.Length % 188);
        for (int offset = 0; offset < media.Length; offset += 188)
        {
            Assert.Equal(0x47, media[offset]);
        }

        MpegTsInspector inspector = new();
        inspector.Push(media.AsSpan(0, 137));
        inspector.Push(media.AsSpan(137));
        inspector.Complete(requireAudio: true, requireTiming: true);
        Assert.True(inspector.PatSeen);
        Assert.True(inspector.PmtSeen);
        Assert.True(inspector.H264Seen);
        Assert.True(inspector.AacSeen);
        Assert.True(inspector.PtsMonotonic);
        Assert.True(inspector.VideoPtsCount > 0);
        Assert.True(inspector.AudioPtsCount > 0);
        Assert.True(inspector.IdrCount >= 2);
        Assert.True(inspector.SpsSeen);
        Assert.True(inspector.PpsSeen);
        Assert.InRange(inspector.MaximumIdrInterval90Khz, 80_000, 100_000);
    }

    [Fact]
    public async Task InvalidTokenIsRejectedWithoutRevealingWhetherSessionExists()
    {
        await using StaticTestClipPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(CreateRenderer(), CancellationToken.None);
        SessionToken invalid = SessionToken.Create();
        Uri invalidUri = new(publication.PublicUri.AbsoluteUri.Replace(
            publication.PublicUri.Segments[^2].TrimEnd('/'),
            invalid.Value,
            StringComparison.Ordinal));
        using HttpClient client = new();

        using HttpResponseMessage response = await client.GetAsync(invalidUri);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StopAsyncIsIdempotentInvalidatesTokenAndClosesPort()
    {
        StaticTestClipPublisher publisher = CreatePublisher();
        StreamPublication publication = await publisher.StartAsync(CreateRenderer(), CancellationToken.None);
        using HttpClient client = new();

        await publisher.StopAsync(CancellationToken.None);
        await publisher.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(publication.PublicUri));
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();
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

    private static RendererDevice CreateRenderer() =>
        new(
            "uuid:test-renderer",
            "Test Renderer",
            "DesktopDlnaCast",
            "Mock",
            IPAddress.Loopback,
            new("http://127.0.0.1/device.xml"));

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
