using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.DependencyInjection;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Media.Interop;
using DesktopDlnaCast.Media.Interop.DependencyInjection;
using DesktopDlnaCast.MockRenderer;
using DesktopDlnaCast.MockRenderer.Diagnostics;
using DesktopDlnaCast.Streaming.DependencyInjection;
using DesktopDlnaCast.Upnp.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopDlnaCast.NativeCastProbe;

internal static partial class Program
{
    private const uint MonitorDefaultToPrimary = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            CastProbeOptions options = CastProbeOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CastProbeOptions.Usage);
                return 0;
            }

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            nint monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
            if (monitor == 0)
            {
                throw new InvalidOperationException("Windows did not return a primary display handle.");
            }

            await using MockRendererHost mockRenderer = new(new()
            {
                HttpPort = 0,
                SsdpPort = 0,
                RequestMethod = MockRendererRequestMethod.Get,
                RequireAudio = options.IncludeAudio,
                RequireVideo = !options.AudioOnly,
                MaximumPullBytes = 512 * 1024 * 1024,
                RejectMetadata = options.RejectMetadata,
            });
            await mockRenderer.StartAsync(cancellation.Token).ConfigureAwait(false);

            ConfigurationManager configuration = new();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Streaming:Port"] = "0",
                ["Streaming:AllowLoopbackForTests"] = "true",
                ["Streaming:RestrictToRendererAddress"] = "true",
                ["Streaming:LiveBufferBytes"] = (12 * 1024 * 1024).ToString(CultureInfo.InvariantCulture),
                ["Streaming:LiveBufferDuration"] = "00:00:05",
                ["Streaming:LiveSubscriberQueueChunks"] = "64",
                ["LiveCast:StartPointTimeout"] = "00:00:15",
                ["LiveCast:PlaybackConfirmationTimeout"] = "00:00:15",
                ["LiveCast:TransportPollInterval"] = "00:00:00.050",
                ["LiveCast:CleanupTimeout"] = "00:00:05",
            });
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDesktopDlnaCastCore(configuration);
            services.AddDesktopDlnaCastUpnp(configuration);
            services.AddDesktopDlnaCastStreaming(configuration);
            services.AddDesktopDlnaCastMediaInterop();
            await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            RendererDevice renderer = new(
                MockRendererOptions.DefaultUdn,
                "DesktopDlnaCast Mock Renderer",
                "DesktopDlnaCast",
                "MockRenderer",
                IPAddress.Loopback,
                new(mockRenderer.BaseUri!, "device.xml"));
            MediaCaptureConfiguration media = new(
                CaptureSourceKind.Display,
                monitor,
                options.IncludeCursor,
                options.Width,
                options.Height,
                30,
                options.VideoBitrate,
                30,
                options.IncludeAudio,
                options.AudioBitrate,
                options.MuteLocalPlayback,
                AudioOnly: options.AudioOnly);
            ICastSession session = provider.GetRequiredService<ICastSession>();
            Stopwatch elapsed = Stopwatch.StartNew();
            int completedIterations = 0;
            for (int iteration = 1; iteration <= options.Iterations; iteration++)
            {
                int validationsBefore = mockRenderer.Events.Snapshot().Count(item =>
                    item.Type == "MediaValidationSucceeded");
                await session.StartAsync(renderer, media, cancellation.Token).ConfigureAwait(false);
                await Task.Delay(options.Duration, cancellation.Token).ConfigureAwait(false);
                await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
                IReadOnlyList<MockRendererEvent> iterationEvents = mockRenderer.Events.Snapshot();
                int validationsAfter = iterationEvents.Count(item =>
                    item.Type == "MediaValidationSucceeded");
                if (validationsAfter != validationsBefore + 1 ||
                    iterationEvents.Any(item => item.Type == "MediaValidationFailed"))
                {
                    throw new InvalidDataException(
                        $"MockRenderer media validation did not succeed in iteration {iteration}.");
                }

                completedIterations++;
            }

            elapsed.Stop();

            IReadOnlyList<MockRendererEvent> events = mockRenderer.Events.Snapshot();
            MockRendererEvent? validation = events.LastOrDefault(item =>
                item.Type == "MediaValidationSucceeded");
            bool validationFailed = events.Any(item => item.Type == "MediaValidationFailed");
            bool valid = validation is not null && !validationFailed &&
                completedIterations == options.Iterations &&
                session.State == CastSessionState.Idle &&
                mockRenderer.TransportState.Equals("STOPPED", StringComparison.OrdinalIgnoreCase);
            WriteResult(new
            {
                status = valid ? "passed" : "failed",
                elapsedSeconds = elapsed.Elapsed.TotalSeconds,
                requestedDurationSeconds = options.Duration.TotalSeconds,
                requestedIterations = options.Iterations,
                completedIterations,
                sessionState = session.State.ToString(),
                rendererTransportState = mockRenderer.TransportState,
                validationBytes = validation?.Data.GetValueOrDefault("bytes"),
                validationPackets = validation?.Data.GetValueOrDefault("packets"),
                metadataFallbacks = events.Count(item => item.Type == "MetadataRejected"),
                rendererRequests = events.Count(item => item.Type == "RendererHttpRequestStarted"),
                firstMediaBytes = events.Count(item => item.Type == "FirstMediaByteReceived"),
                mediaValidationFailures = events.Count(item => item.Type == "MediaValidationFailed"),
                encoder = session.EncoderDiagnostics,
                eventCount = events.Count,
            });
            return valid ? 0 : 5;
        }
        catch (OperationCanceledException)
        {
            WriteResult(new { status = "canceled" });
            return 130;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string message = RedactTokenRegex().Replace(exception.Message, "/stream/<redacted>/");
            if (message.Length > 4096)
            {
                message = message[..4096];
            }

            WriteResult(new
            {
                status = "error",
                errorType = exception.GetType().Name,
                message,
            });
            return 1;
        }
    }

    private static void WriteResult<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    [GeneratedRegex(@"/stream/[^/\s]+/", RegexOptions.CultureInvariant)]
    private static partial Regex RedactTokenRegex();

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}

internal sealed record CastProbeOptions(
    TimeSpan Duration,
    int Iterations,
    int Width,
    int Height,
    int VideoBitrate,
    int AudioBitrate,
    bool IncludeCursor,
    bool IncludeAudio,
    bool AudioOnly,
    bool MuteLocalPlayback,
    bool RejectMetadata,
    bool ShowHelp)
{
    public const string Usage =
        "Usage: DesktopDlnaCast.NativeCastProbe [--duration-seconds 10] " +
        "[--iterations 1] " +
        "[--width 1280] [--height 720] [--video-bitrate 3000000] " +
        "[--audio-bitrate 128000] [--include-cursor true|false] " +
        "[--include-audio true|false] [--audio-only true|false] " +
        "[--mute-local-playback true|false] [--reject-metadata]";

    public static CastProbeOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Any(static value => value is "--help" or "-h"))
        {
            return new(TimeSpan.Zero, 0, 0, 0, 0, 0, false, false, false, false, false, true);
        }

        int durationSeconds = 10;
        int iterations = 1;
        int width = 1280;
        int height = 720;
        int videoBitrate = 3_000_000;
        int audioBitrate = 128_000;
        bool includeCursor = true;
        bool includeAudio = true;
        bool audioOnly = false;
        bool muteLocalPlayback = false;
        bool rejectMetadata = false;
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--duration-seconds":
                    durationSeconds = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--iterations":
                    iterations = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--width":
                    width = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--height":
                    height = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--video-bitrate":
                    videoBitrate = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--audio-bitrate":
                    audioBitrate = ParseInteger(NextValue(args, ref index, argument), argument);
                    break;
                case "--include-cursor":
                    includeCursor = ParseBoolean(NextValue(args, ref index, argument), argument);
                    break;
                case "--include-audio":
                    includeAudio = ParseBoolean(NextValue(args, ref index, argument), argument);
                    break;
                case "--audio-only":
                    audioOnly = ParseBoolean(NextValue(args, ref index, argument), argument);
                    break;
                case "--mute-local-playback":
                    muteLocalPlayback = ParseBoolean(NextValue(args, ref index, argument), argument);
                    break;
                case "--reject-metadata":
                    rejectMetadata = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'. {Usage}");
            }
        }

        if (durationSeconds is < 1 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--duration-seconds must be between 1 and 180.");
        }

        if (iterations is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--iterations must be between 1 and 20.");
        }

        ValidateEvenDimension(width, 2, 7680, "--width");
        ValidateEvenDimension(height, 2, 4320, "--height");
        if (videoBitrate is < 100_000 or > 12_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--video-bitrate must be between 100000 and 12000000.");
        }
        if (audioBitrate is < 32_000 or > 512_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--audio-bitrate must be between 32000 and 512000.");
        }
        if (muteLocalPlayback && !includeAudio)
        {
            throw new ArgumentException("--mute-local-playback requires --include-audio true.");
        }
        if (audioOnly && !includeAudio)
        {
            throw new ArgumentException("--audio-only requires --include-audio true.");
        }

        return new(
            TimeSpan.FromSeconds(durationSeconds),
            iterations,
            width,
            height,
            videoBitrate,
            audioBitrate,
            includeCursor,
            includeAudio,
            audioOnly,
            muteLocalPlayback,
            rejectMetadata,
            false);
    }

    private static string NextValue(IReadOnlyList<string> args, ref int index, string argument)
    {
        if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{argument} requires a value.");
        }

        return args[index];
    }

    private static int ParseInteger(string value, string argument) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new ArgumentException($"{argument} requires an integer value.");

    private static bool ParseBoolean(string value, string argument) =>
        bool.TryParse(value, out bool result)
            ? result
            : throw new ArgumentException($"{argument} requires true or false.");

    private static void ValidateEvenDimension(int value, int minimum, int maximum, string argument)
    {
        if (value < minimum || value > maximum || (value & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"{argument} must be even and between {minimum} and {maximum}.");
        }
    }
}
