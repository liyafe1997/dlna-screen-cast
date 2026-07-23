using System.Net;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Streaming.Buffering;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Diagnostics;
using DesktopDlnaCast.Streaming.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DesktopDlnaCast.Streaming.Publishing;

public sealed class ContinuousMpegTsPublisher(
    INetworkInterfaceSelector networkSelector,
    IOptions<StreamingOptions> options,
    ILogger<ContinuousMpegTsPublisher> logger) : ILiveStreamPublisher
{
    private static readonly Action<ILogger, IPAddress, int, Exception?> LogServerBound =
        LoggerMessage.Define<IPAddress, int>(
            LogLevel.Information,
            new EventId(210, nameof(LogServerBound)),
            "Continuous MPEG-TS server bound to {LocalAddress}:{Port}");
    private static readonly Action<ILogger, string, IPAddress, Exception?> LogRendererRequest =
        LoggerMessage.Define<string, IPAddress>(
            LogLevel.Information,
            new EventId(211, nameof(LogRendererRequest)),
            "Renderer live HTTP request received: {Method} from {RemoteAddress}");
    private static readonly Action<ILogger, string, string, Exception?> LogRendererHeader =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(212, nameof(LogRendererHeader)),
            "Renderer HTTP header {HeaderName}: {HeaderValue}");
    private static readonly Action<ILogger, Exception?> LogServerStopped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(213, nameof(LogServerStopped)),
            "Continuous MPEG-TS server stopped and its token was invalidated");

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private WebApplication? application;
    private LiveStreamBuffer? buffer;
    private LiveStreamPublishOptions activePublishOptions = new();
    private SessionToken? activeToken;
    private IPAddress? rendererAddress;
    private StreamPublication? publication;
    private TaskCompletionSource<StreamClientRequest>? requestObserved;
    private TaskCompletionSource? startPointObserved;
    private bool disposed;

    public async Task<StreamPublication> StartAsync(
        RendererDevice renderer,
        LiveStreamPublishOptions publishOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(publishOptions);
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (publication is not null)
            {
                return publication;
            }

            ValidateOptions(options.Value);
            IPAddress localAddress = await networkSelector.SelectLocalAddressAsync(
                renderer.Address,
                cancellationToken).ConfigureAwait(false);
            if (localAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                localAddress.Equals(IPAddress.Any))
            {
                throw new InvalidOperationException("The selected stream address is not a concrete IPv4 address.");
            }

            SessionToken token = SessionToken.Create();
            LiveStreamBuffer streamBuffer = new(
                options.Value.LiveBufferBytes,
                options.Value.LiveBufferDuration,
                options.Value.LiveSubscriberQueueChunks);
            WebApplication app = CreateApplication(localAddress, options.Value.Port);
            activeToken = token;
            rendererAddress = renderer.Address;
            buffer = streamBuffer;
            activePublishOptions = publishOptions;
            requestObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
            startPointObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
            MapEndpoints(app);
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                int boundPort = GetBoundPort(app);
                (string extension, string contentType, string protocolInfo) =
                    GetPublicationDetails(publishOptions.AudioProfile);
                Uri publicUri = new(
                    $"http://{localAddress}:{boundPort}/stream/{token.Value}/live.{extension}");
                publication = new(
                    publicUri,
                    $"http://{localAddress}:{boundPort}/stream/{token.Redacted}/live.{extension}",
                    StreamMode.MpegTsContinuous,
                    contentType,
                    protocolInfo);
                application = app;
                LogServerBound(logger, localAddress, boundPort, null);
                return publication;
            }
            catch
            {
                activeToken = null;
                rendererAddress = null;
                buffer = null;
                requestObserved = null;
                startPointObserved = null;
                streamBuffer.Dispose();
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public ValueTask PublishAsync(MediaStreamChunk chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LiveStreamBuffer target = buffer ??
            throw new InvalidOperationException("The continuous stream publisher is not running.");
        target.Append(chunk);
        if (chunk.StartsAtRandomAccessPoint)
        {
            startPointObserved?.TrySetResult();
        }

        return ValueTask.CompletedTask;
    }

    public async Task WaitForStartPointAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Task task = startPointObserved?.Task ??
            throw new InvalidOperationException("The continuous stream publisher is not running.");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        await task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
    }

    public void Complete(Exception? completionException = null) => buffer?.Complete(completionException);

    public async Task<StreamClientRequest> WaitForClientRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Task<StreamClientRequest> task = requestObserved?.Task ??
            throw new InvalidOperationException("The continuous stream publisher is not running.");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return await task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WebApplication? app = application;
            LiveStreamBuffer? streamBuffer = buffer;
            activeToken = null;
            rendererAddress = null;
            publication = null;
            application = null;
            buffer = null;
            requestObserved?.TrySetCanceled(cancellationToken);
            requestObserved = null;
            startPointObserved?.TrySetCanceled(cancellationToken);
            startPointObserved = null;
            streamBuffer?.Complete();
            try
            {
                if (app is not null)
                {
                    try
                    {
                        await app.StopAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        await app.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                streamBuffer?.Dispose();
                if (app is not null)
                {
                    LogServerStopped(logger, null);
                }
            }
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

    private static WebApplication CreateApplication(IPAddress localAddress, int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ContinuousMpegTsPublisher).Assembly.GetName().Name,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(localAddress, port));
        return builder.Build();
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Text("ok", "text/plain"));
        app.MapMethods(
            "/stream/{token}/live.{extension}",
            [HttpMethods.Get, HttpMethods.Head],
            HandleMediaRequestAsync);
    }

    private async Task HandleMediaRequestAsync(HttpContext context)
    {
        string? candidate = context.Request.RouteValues["token"]?.ToString();
        SessionToken? token = activeToken;
        IPAddress? remoteAddress = NormalizeAddress(context.Connection.RemoteIpAddress);
        IPAddress? allowedAddress = NormalizeAddress(rendererAddress);
        LiveStreamBuffer? streamBuffer = buffer;
        if (token is null || streamBuffer is null || !token.Value.FixedTimeEquals(candidate) ||
            remoteAddress is null ||
            (options.Value.RestrictToRendererAddress && !remoteAddress.Equals(allowedAddress)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        StreamClientRequest observation = new(
            context.Request.Method,
            remoteAddress,
            DateTimeOffset.UtcNow);
        requestObserved?.TrySetResult(observation);
        LogRendererRequest(logger, observation.Method, observation.RemoteAddress, null);
        foreach ((string name, string value) in HttpRequestHeaderSanitizer.EnumerateSafe(context.Request.Headers))
        {
            LogRendererHeader(logger, name, value, null);
        }

        StreamPublication? activePublication = publication;
        if (activePublication is null ||
            !string.Equals(
                context.Request.RouteValues["extension"]?.ToString(),
                Path.GetExtension(activePublication.PublicUri.AbsolutePath).TrimStart('.'),
                StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = activePublication.ContentType;
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers["transferMode.dlna.org"] = "Streaming";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        LiveStreamSubscription subscription;
        try
        {
            subscription = streamBuffer.Subscribe(activePublishOptions.StartAtLiveEdge);
        }
        catch (InvalidOperationException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        await using (subscription.ConfigureAwait(false))
        {
            await foreach (MediaStreamChunk chunk in subscription.ReadAllAsync(context.RequestAborted)
                               .ConfigureAwait(false))
            {
                await context.Response.Body.WriteAsync(chunk.Data, context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    private static int GetBoundPort(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature feature = server.Features.Get<IServerAddressesFeature>() ??
            throw new InvalidOperationException("Kestrel did not expose its bound addresses.");
        return new Uri(feature.Addresses.Single()).Port;
    }

    private static IPAddress? NormalizeAddress(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

    private static (string Extension, string ContentType, string ProtocolInfo)
        GetPublicationDetails(AudioCastProfile profile) =>
        profile switch
        {
            AudioCastProfile.AacAdts =>
                ("aac", "audio/vnd.dlna.adts",
                    "http-get:*:audio/vnd.dlna.adts:DLNA.ORG_PN=AAC_ADTS"),
            AudioCastProfile.Mp3 =>
                ("mp3", "audio/mpeg", "http-get:*:audio/mpeg:DLNA.ORG_PN=MP3"),
            AudioCastProfile.Lpcm =>
                ("l16", "audio/L16;rate=48000;channels=2",
                    "http-get:*:audio/L16;rate=48000;channels=2:DLNA.ORG_PN=LPCM"),
            AudioCastProfile.AacMpegTsCompatibility =>
                ("ts", "video/mpeg", "http-get:*:video/mpeg:*"),
            _ => ("ts", "video/mpeg", "http-get:*:video/mpeg:*"),
        };

    private static void ValidateOptions(StreamingOptions value)
    {
        if (value.Port is < 0 or > 65535 ||
            value.LiveBufferBytes is < 188 or > 256 * 1024 * 1024 ||
            value.LiveBufferDuration <= TimeSpan.Zero ||
            value.LiveBufferDuration > TimeSpan.FromSeconds(30) ||
            value.LiveSubscriberQueueChunks is < 1 or > 4096)
        {
            throw new InvalidOperationException("The continuous streaming configuration is invalid.");
        }
    }
}
