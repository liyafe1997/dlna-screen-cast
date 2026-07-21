using System.Net;
using System.Reflection;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
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

public sealed class StaticTestClipPublisher(
    INetworkInterfaceSelector networkSelector,
    IOptions<StreamingOptions> options,
    ILogger<StaticTestClipPublisher> logger) : IStreamPublisher
{
    private const string ResourceName = "DesktopDlnaCast.Streaming.Assets.test-pattern.ts";
    private static readonly Lazy<byte[]> TestClip = new(LoadTestClip, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Action<ILogger, IPAddress, int, Exception?> LogServerBound =
        LoggerMessage.Define<IPAddress, int>(
            LogLevel.Information,
            new EventId(200, nameof(LogServerBound)),
            "Static test stream server bound to {LocalAddress}:{Port}");
    private static readonly Action<ILogger, string, IPAddress, Exception?> LogRendererRequest =
        LoggerMessage.Define<string, IPAddress>(
            LogLevel.Information,
            new EventId(201, nameof(LogRendererRequest)),
            "Renderer HTTP request received: {Method} from {RemoteAddress}");
    private static readonly Action<ILogger, Exception?> LogServerStopped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(202, nameof(LogServerStopped)),
            "Static test stream server stopped and its session token was invalidated");
    private static readonly Action<ILogger, string, string, Exception?> LogRendererHeader =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(203, nameof(LogRendererHeader)),
            "Renderer HTTP header {HeaderName}: {HeaderValue}");

    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private WebApplication? application;
    private SessionToken? activeToken;
    private IPAddress? rendererAddress;
    private StreamPublication? publication;
    private TaskCompletionSource<StreamClientRequest>? requestObserved;
    private bool disposed;

    public async Task<StreamPublication> StartAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(renderer);
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

            SessionToken sessionToken = SessionToken.Create();
            WebApplication app = CreateApplication(localAddress, options.Value.Port);
            rendererAddress = renderer.Address;
            activeToken = sessionToken;
            requestObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
            MapEndpoints(app);
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                int boundPort = GetBoundPort(app);
                Uri publicUri = new($"http://{localAddress}:{boundPort}/stream/{sessionToken.Value}/test.ts");
                publication = new(
                    publicUri,
                    $"http://{localAddress}:{boundPort}/stream/{sessionToken.Redacted}/test.ts",
                    StreamMode.MpegTsContinuous);
                application = app;
                LogServerBound(logger, localAddress, boundPort, null);
                return publication;
            }
            catch
            {
                activeToken = null;
                rendererAddress = null;
                requestObserved = null;
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task<StreamClientRequest> WaitForClientRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Task<StreamClientRequest> task = requestObserved?.Task ??
            throw new InvalidOperationException("The static test stream is not running.");
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
            activeToken = null;
            rendererAddress = null;
            publication = null;
            requestObserved?.TrySetCanceled(cancellationToken);
            requestObserved = null;
            application = null;
            if (app is null)
            {
                return;
            }

            try
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
                LogServerStopped(logger, null);
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
            ApplicationName = typeof(StaticTestClipPublisher).Assembly.GetName().Name,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(localAddress, port));
        return builder.Build();
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Text("ok", "text/plain"));
        app.MapMethods(
            "/stream/{token}/test.ts",
            [HttpMethods.Get, HttpMethods.Head],
            HandleMediaRequestAsync);
    }

    private async Task HandleMediaRequestAsync(HttpContext context)
    {
        string? candidate = context.Request.RouteValues["token"]?.ToString();
        SessionToken? token = activeToken;
        IPAddress? remoteAddress = NormalizeAddress(context.Connection.RemoteIpAddress);
        IPAddress? allowedAddress = NormalizeAddress(rendererAddress);
        if (token is null || !token.Value.FixedTimeEquals(candidate) || remoteAddress is null ||
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

        context.Response.ContentType = "video/mpeg";
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.ContentLength = TestClip.Value.Length;
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.Body.WriteAsync(TestClip.Value, context.RequestAborted).ConfigureAwait(false);
    }

    private static int GetBoundPort(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature feature = server.Features.Get<IServerAddressesFeature>() ??
            throw new InvalidOperationException("Kestrel did not expose its bound addresses.");
        string address = feature.Addresses.Single();
        return new Uri(address).Port;
    }

    private static IPAddress? NormalizeAddress(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

    private static byte[] LoadTestClip()
    {
        Assembly assembly = typeof(StaticTestClipPublisher).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException("The embedded static MPEG-TS test clip is missing.");
        if (stream.Length is <= 0 or > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("The embedded static MPEG-TS test clip has an invalid size.");
        }

        using MemoryStream output = new(checked((int)stream.Length));
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static void ValidateOptions(StreamingOptions value)
    {
        if (value.Port is < 0 or > 65535)
        {
            throw new InvalidOperationException("The configured streaming port is invalid.");
        }
    }
}
