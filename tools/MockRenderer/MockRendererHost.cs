using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using DesktopDlnaCast.MockRenderer.Diagnostics;
using DesktopDlnaCast.MockRenderer.Discovery;
using DesktopDlnaCast.MockRenderer.Soap;
using DesktopDlnaCast.Streaming.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.MockRenderer;

public sealed class MockRendererHost(MockRendererOptions options) : IAsyncDisposable
{
    public const string AvTransportServiceType = "urn:schemas-upnp-org:service:AVTransport:3";
    public const string ConnectionManagerServiceType = "urn:schemas-upnp-org:service:ConnectionManager:3";
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly object stateLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient pullClient = CreatePullClient();
    private WebApplication? application;
    private MockSsdpResponder? ssdpResponder;
    private CancellationTokenSource? pullCancellation;
    private Task? pullTask;
    private Uri? currentUri;
    private string currentMetadata = string.Empty;
    private string transportState = "STOPPED";
    private bool disposed;

    public MockRendererEventStore Events { get; } = new();

    public event EventHandler? ShutdownRequested;

    public Uri? BaseUri { get; private set; }

    public IPEndPoint? SsdpEndPoint => ssdpResponder?.EndPoint;

    public string TransportState
    {
        get
        {
            lock (stateLock)
            {
                return options.ForcedTransportState ?? transportState;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (application is not null)
            {
                return;
            }

            ValidateOptions(options);
            WebApplication app = CreateApplication();
            MapEndpoints(app);
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                int port = GetBoundPort(app);
                BaseUri = new($"http://{options.ListenAddress}:{port}/");
                MockSsdpResponder responder = new(
                    options.ListenAddress,
                    options.SsdpPort,
                    options.Udn,
                    new(BaseUri, "device.xml"),
                    Events);
                responder.Start();
                ssdpResponder = responder;
                application = app;
                Events.Record(
                    "RendererReady",
                    ("http", BaseUri.AbsoluteUri),
                    ("ssdp", responder.EndPoint.ToString()),
                    ("udn", options.Udn));
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            await StopPullAsync(setStoppedState: true).ConfigureAwait(false);
            if (ssdpResponder is not null)
            {
                await ssdpResponder.DisposeAsync().ConfigureAwait(false);
                ssdpResponder = null;
            }

            if (application is not null)
            {
                try
                {
                    await application.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                    application = null;
                }
            }

            pullClient.Dispose();
            lifetime.Dispose();
            Events.Record("RendererStopped");
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private WebApplication CreateApplication()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MockRendererHost).Assembly.GetName().Name,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(options.ListenAddress, options.HttpPort));
        return builder.Build();
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ready",
            httpPort = BaseUri?.Port,
            ssdpPort = SsdpEndPoint?.Port,
            udn = options.Udn,
        }));
        app.MapGet("/device.xml", HandleDeviceDescription);
        app.MapPost("/upnp/avtransport/control", HandleAvTransportAsync);
        app.MapPost("/upnp/connection/control", HandleConnectionManagerAsync);
        app.MapGet("/test/events", () => Results.Json(Events.Snapshot()));
        app.MapGet("/test/state", () => Results.Json(new
        {
            transportState = TransportState,
            currentUri = currentUri is null ? null : DiagnosticRedactor.RedactTokens(currentUri.AbsoluteUri),
            metadataLength = currentMetadata.Length,
        }));
        app.MapPost("/test/shutdown", (HttpContext context) =>
        {
            Events.Record("ShutdownRequested");
            context.Response.OnCompleted(() =>
            {
                ShutdownRequested?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            });
            return Results.Accepted();
        });
    }

    private IResult HandleDeviceDescription()
    {
        string xml = CreateDeviceDescription();
        Events.Record("DeviceDescriptionRequested");
        return Results.Text(xml, "text/xml", Encoding.UTF8);
    }

    private async Task HandleAvTransportAsync(HttpContext context)
    {
        MockSoapRequest request;
        try
        {
            request = await MockSoapCodec.ReadRequestAsync(context.Request, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }
        catch (FormatException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }
        catch (XmlException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }

        Events.Record(
            "SoapActionReceived",
            ("action", request.Action),
            ("serviceType", request.ServiceType),
            ("soapActionHeader", context.Request.Headers["SOAPAction"].ToString()));
        if (!request.ServiceType.Equals(AvTransportServiceType, StringComparison.Ordinal) ||
            options.FaultAction?.Equals(request.Action, StringComparison.OrdinalIgnoreCase) == true)
        {
            await WriteFaultAsync(context, 501, "Action Failed").ConfigureAwait(false);
            return;
        }

        switch (request.Action)
        {
            case "SetAVTransportURI":
                await HandleSetTransportUriAsync(context, request).ConfigureAwait(false);
                break;
            case "Play":
                await HandlePlayAsync(context).ConfigureAwait(false);
                break;
            case "Stop":
                await StopPullAsync(setStoppedState: true).ConfigureAwait(false);
                Events.Record("StopCompleted");
                await WriteActionResponseAsync(context, request.Action, AvTransportServiceType, [])
                    .ConfigureAwait(false);
                break;
            case "GetTransportInfo":
                await WriteActionResponseAsync(
                    context,
                    request.Action,
                    AvTransportServiceType,
                    new Dictionary<string, string?>
                    {
                        ["CurrentTransportState"] = TransportState,
                        ["CurrentTransportStatus"] = "OK",
                        ["CurrentSpeed"] = "1",
                    }).ConfigureAwait(false);
                break;
            default:
                await WriteFaultAsync(context, 401, "Invalid Action").ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleConnectionManagerAsync(HttpContext context)
    {
        MockSoapRequest request;
        try
        {
            request = await MockSoapCodec.ReadRequestAsync(context.Request, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }
        catch (FormatException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }
        catch (XmlException)
        {
            await WriteFaultAsync(context, 402, "Invalid Args").ConfigureAwait(false);
            return;
        }

        Events.Record(
            "SoapActionReceived",
            ("action", request.Action),
            ("serviceType", request.ServiceType),
            ("soapActionHeader", context.Request.Headers["SOAPAction"].ToString()));
        if (!request.ServiceType.Equals(ConnectionManagerServiceType, StringComparison.Ordinal) ||
            !request.Action.Equals("GetProtocolInfo", StringComparison.Ordinal))
        {
            await WriteFaultAsync(context, 401, "Invalid Action").ConfigureAwait(false);
            return;
        }

        await WriteActionResponseAsync(
            context,
            request.Action,
            ConnectionManagerServiceType,
            new Dictionary<string, string?>
            {
                ["Source"] = string.Empty,
                ["Sink"] = options.SinkProtocolInfo,
            }).ConfigureAwait(false);
    }

    private async Task HandleSetTransportUriAsync(HttpContext context, MockSoapRequest request)
    {
        if (!request.Arguments.TryGetValue("CurrentURI", out string? uriText) ||
            uriText.Length > 8192 ||
            !Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            await WriteFaultAsync(context, 714, "Illegal MIME-type").ConfigureAwait(false);
            return;
        }

        string metadata = request.Arguments.GetValueOrDefault("CurrentURIMetaData") ?? string.Empty;
        if (options.RejectMetadata && metadata.Length > 0)
        {
            Events.Record("MetadataRejected", ("metadataLength", metadata.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await WriteFaultAsync(context, 714, "Metadata rejected by injected fault").ConfigureAwait(false);
            return;
        }

        lock (stateLock)
        {
            currentUri = uri;
            currentMetadata = metadata;
        }

        Events.Record(
            "TransportUriSet",
            ("uri", uri.AbsoluteUri),
            ("metadataLength", metadata.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await WriteActionResponseAsync(context, request.Action, AvTransportServiceType, []).ConfigureAwait(false);
    }

    private async Task HandlePlayAsync(HttpContext context)
    {
        Uri? uri;
        lock (stateLock)
        {
            uri = currentUri;
        }

        if (uri is null)
        {
            await WriteFaultAsync(context, 716, "Resource not found").ConfigureAwait(false);
            return;
        }

        Events.Record("PlayStarted");
        await StopPullAsync(setStoppedState: false).ConfigureAwait(false);
        SetTransportState("TRANSITIONING");
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Task startedPull = PullMediaAsync(uri, cancellation.Token);
        lock (stateLock)
        {
            pullCancellation = cancellation;
            pullTask = startedPull;
        }

        Events.Record("PlayCompleted");
        await WriteActionResponseAsync(context, "Play", AvTransportServiceType, []).ConfigureAwait(false);
    }

    private async Task PullMediaAsync(Uri uri, CancellationToken cancellationToken)
    {
        MpegTsInspector? inspector = null;
        AudioPayloadValidator? audioValidator = null;
        int total = 0;
        try
        {
            if (options.PullDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.PullDelay, cancellationToken).ConfigureAwait(false);
            }

            HttpMethod method = options.RequestMethod == MockRendererRequestMethod.Head
                ? HttpMethod.Head
                : HttpMethod.Get;
            using HttpRequestMessage request = new(method, uri);
            foreach ((string name, string value) in options.RequestHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
                Events.Record(
                    "RendererHttpRequestHeader",
                    ("name", name),
                    ("value", IsSensitiveHeader(name) ? "<redacted>" : value));
            }

            Events.Record("RendererHttpRequestStarted", ("method", method.Method), ("uri", uri.AbsoluteUri));
            Stopwatch stopwatch = Stopwatch.StartNew();
            using HttpResponseMessage response = await pullClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            Events.Record(
                "RendererHttpResponseReceived",
                ("status", ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("contentType", response.Content.Headers.ContentType?.MediaType));
            if (!response.IsSuccessStatusCode)
            {
                SetTransportState("STOPPED");
                return;
            }

            if (method == HttpMethod.Head)
            {
                SetTransportState("PLAYING");
                Events.Record("RendererHttpCompleted", ("reason", "HeadCompleted"));
                return;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[16 * 1024];
            string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                audioValidator = new(contentType);
            }
            else
            {
                inspector = new();
            }

            bool firstByteObserved = false;
            while (total < options.MaximumPullBytes)
            {
                int maximumRead = Math.Min(buffer.Length, options.MaximumPullBytes - total);
                int read = await stream.ReadAsync(buffer.AsMemory(0, maximumRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    Events.Record("RendererHttpCompleted", ("reason", "EndOfStream"));
                    break;
                }

                if (!firstByteObserved)
                {
                    firstByteObserved = true;
                    Events.Record(
                        "FirstMediaByteReceived",
                        ("elapsedMilliseconds", stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }

                inspector?.Push(buffer.AsSpan(0, read));
                audioValidator?.Push(buffer.AsSpan(0, read));
                total += read;
                if (total >= (audioValidator is null ? 188 : 1))
                {
                    SetTransportState("PLAYING");
                }

                if (options.DisconnectAfterBytes is int disconnectAfter && total >= disconnectAfter)
                {
                    Events.Record("RendererHttpCompleted", ("reason", "InjectedDisconnect"));
                    break;
                }
            }

            if (total >= options.MaximumPullBytes)
            {
                Events.Record("RendererHttpCompleted", ("reason", "ReadLimitReached"));
            }

            if (inspector is not null)
            {
                RecordMediaValidation(inspector, total, allowTrailingPartialPacket: false);
            }
            else
            {
                RecordAudioValidation(audioValidator!, total);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Events.Record("RendererHttpCompleted", ("reason", "Canceled"));
            if (inspector is not null && total > 0)
            {
                try
                {
                    RecordMediaValidation(inspector, total, allowTrailingPartialPacket: true);
                }
                catch (InvalidDataException exception)
                {
                    Events.Record("MediaValidationFailed", ("error", exception.Message));
                }
            }
            else if (audioValidator is not null && total > 0)
            {
                try
                {
                    RecordAudioValidation(audioValidator, total);
                }
                catch (InvalidDataException exception)
                {
                    Events.Record("MediaValidationFailed", ("error", exception.Message));
                }
            }
        }
        catch (HttpRequestException exception)
        {
            SetTransportState("STOPPED");
            Events.Record("RendererHttpFailed", ("error", exception.Message));
        }
        catch (InvalidDataException exception)
        {
            SetTransportState("STOPPED");
            Events.Record("MediaValidationFailed", ("error", exception.Message));
        }
    }

    private void RecordMediaValidation(
        MpegTsInspector inspector,
        int total,
        bool allowTrailingPartialPacket)
    {
        inspector.Complete(
            options.RequireAudio,
            allowTrailingPartialPacket: allowTrailingPartialPacket,
            requireVideo: options.RequireVideo);
        Events.Record(
            "MediaValidationSucceeded",
            ("bytes", total.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("packets", inspector.PacketCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("h264", inspector.H264Seen.ToString()),
            ("aac", inspector.AacSeen.ToString()));
    }

    private void RecordAudioValidation(AudioPayloadValidator validator, int total)
    {
        validator.Complete();
        Events.Record(
            "MediaValidationSucceeded",
            ("bytes", total.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("audioFormat", validator.ContentType));
    }

    private async Task StopPullAsync(bool setStoppedState)
    {
        CancellationTokenSource? cancellation;
        Task? runningTask;
        lock (stateLock)
        {
            cancellation = pullCancellation;
            runningTask = pullTask;
            pullCancellation = null;
            pullTask = null;
            if (setStoppedState)
            {
                transportState = "STOPPED";
            }
        }

        cancellation?.Cancel();
        if (runningTask is not null)
        {
            await runningTask.ConfigureAwait(false);
        }

        cancellation?.Dispose();
        if (setStoppedState)
        {
            Events.Record("TransportStateChanged", ("state", "STOPPED"));
        }
    }

    private void SetTransportState(string state)
    {
        lock (stateLock)
        {
            if (transportState == state)
            {
                return;
            }

            transportState = state;
        }

        Events.Record("TransportStateChanged", ("state", state));
    }

    private string CreateDeviceDescription()
    {
        StringBuilder output = new();
        XmlWriterSettings settings = new() { OmitXmlDeclaration = true, Indent = true };
        using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            const string deviceNamespace = "urn:schemas-upnp-org:device-1-0";
            writer.WriteStartElement("root", deviceNamespace);
            writer.WriteStartElement("specVersion", deviceNamespace);
            writer.WriteElementString("major", deviceNamespace, "1");
            writer.WriteElementString("minor", deviceNamespace, "1");
            writer.WriteEndElement();
            writer.WriteStartElement("device", deviceNamespace);
            writer.WriteElementString("deviceType", deviceNamespace, "urn:schemas-upnp-org:device:MediaRenderer:1");
            writer.WriteElementString("friendlyName", deviceNamespace, options.FriendlyName);
            writer.WriteElementString("manufacturer", deviceNamespace, "DesktopDlnaCast");
            writer.WriteElementString("modelName", deviceNamespace, "MockRenderer");
            writer.WriteElementString("UDN", deviceNamespace, options.Udn);
            writer.WriteStartElement("serviceList", deviceNamespace);
            WriteService(writer, deviceNamespace, AvTransportServiceType, "urn:upnp-org:serviceId:AVTransport", "/upnp/avtransport/control");
            WriteService(writer, deviceNamespace, ConnectionManagerServiceType, "urn:upnp-org:serviceId:ConnectionManager", "/upnp/connection/control");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return output.ToString();
    }

    private static void WriteService(
        XmlWriter writer,
        string deviceNamespace,
        string serviceType,
        string serviceId,
        string controlUrl)
    {
        writer.WriteStartElement("service", deviceNamespace);
        writer.WriteElementString("serviceType", deviceNamespace, serviceType);
        writer.WriteElementString("serviceId", deviceNamespace, serviceId);
        writer.WriteElementString("controlURL", deviceNamespace, controlUrl);
        writer.WriteEndElement();
    }

    private static async Task WriteActionResponseAsync(
        HttpContext context,
        string action,
        string serviceType,
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        string xml = MockSoapCodec.CreateActionResponse(serviceType, action, values);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/xml; charset=utf-8";
        await context.Response.WriteAsync(xml, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteFaultAsync(HttpContext context, int errorCode, string description)
    {
        string xml = MockSoapCodec.CreateFault(errorCode, description);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/xml; charset=utf-8";
        await context.Response.WriteAsync(xml, context.RequestAborted).ConfigureAwait(false);
    }

    private static int GetBoundPort(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature feature = server.Features.Get<IServerAddressesFeature>() ??
            throw new InvalidOperationException("Kestrel did not expose its bound addresses.");
        return new Uri(feature.Addresses.Single()).Port;
    }

    private static HttpClient CreatePullClient()
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        return new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase);

    private sealed class AudioPayloadValidator(string contentType)
    {
        private readonly byte[] prefix = new byte[4];
        private int prefixLength;
        private int totalBytes;

        public string ContentType { get; } = contentType;

        public void Push(ReadOnlySpan<byte> bytes)
        {
            int copy = Math.Min(prefix.Length - prefixLength, bytes.Length);
            bytes[..copy].CopyTo(prefix.AsSpan(prefixLength));
            prefixLength += copy;
            totalBytes += bytes.Length;
        }

        public void Complete()
        {
            if (totalBytes <= 0)
            {
                throw new InvalidDataException("The audio stream did not contain media bytes.");
            }

            if (ContentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) &&
                (prefixLength < 2 || prefix[0] != 0xFF || (prefix[1] & 0xE0) != 0xE0))
            {
                throw new InvalidDataException("The audio/mpeg stream does not start with an MP3 frame.");
            }

            if ((ContentType.Equals("audio/vnd.dlna.adts", StringComparison.OrdinalIgnoreCase) ||
                 ContentType.Equals("audio/aac", StringComparison.OrdinalIgnoreCase)) &&
                (prefixLength < 2 || prefix[0] != 0xFF || (prefix[1] & 0xF6) != 0xF0))
            {
                throw new InvalidDataException("The AAC stream does not start with an ADTS frame.");
            }

            if (ContentType.Equals("audio/L16", StringComparison.OrdinalIgnoreCase) &&
                totalBytes % 2 != 0)
            {
                throw new InvalidDataException("The audio/L16 stream is not 16-bit sample aligned.");
            }
        }
    }

    private static void ValidateOptions(MockRendererOptions value)
    {
        if ((!IPAddress.IsLoopback(value.ListenAddress) && !value.AllowNonLoopback) ||
            value.ListenAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            value.HttpPort is < 0 or > 65535 ||
            value.SsdpPort is < 0 or > 65535 ||
            value.PullDelay < TimeSpan.Zero || value.PullDelay > TimeSpan.FromMinutes(1) ||
            value.DisconnectAfterBytes is <= 0 ||
            value.MaximumPullBytes is < 188 or > 512 * 1024 * 1024 ||
            value.Udn.Length is 0 or > 512 ||
            value.FriendlyName.Length is 0 or > 1024 ||
            string.IsNullOrWhiteSpace(value.SinkProtocolInfo) ||
            value.SinkProtocolInfo.Length > 16 * 1024 ||
            value.RequestHeaders.Count > 32 ||
            value.RequestHeaders.Any(pair =>
                pair.Key.Length is 0 or > 128 || pair.Value.Length > 4096 ||
                pair.Key.Any(char.IsControl) || pair.Value.Any(char.IsControl)))
        {
            throw new InvalidOperationException("The MockRenderer configuration is invalid.");
        }
    }
}
