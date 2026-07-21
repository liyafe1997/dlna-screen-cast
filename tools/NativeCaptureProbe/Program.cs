using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Media.Interop;

namespace DesktopDlnaCast.NativeCaptureProbe;

internal static partial class Program
{
    private const uint MonitorDefaultToPrimary = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            ProbeOptions options = ProbeOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(ProbeOptions.Usage);
                return 0;
            }

            using CancellationTokenSource userCancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                userCancellation.Cancel();
            };

            nint monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
            if (monitor == 0)
            {
                throw new InvalidOperationException("Windows did not return a primary display handle.");
            }

            MediaCaptureConfiguration configuration = new(
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
                options.MuteLocalPlayback);
            NativeMediaCaptureSessionFactory factory = new();
            await using var session = await factory.CreateAsync(
                configuration,
                userCancellation.Token).ConfigureAwait(false);
            await session.StartAsync(userCancellation.Token).ConfigureAwait(false);

            FileMode fileMode = options.Overwrite ? FileMode.Create : FileMode.CreateNew;
            await using FileStream output = new(
                options.OutputPath,
                fileMode,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using CancellationTokenSource durationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token);
            durationCancellation.CancelAfter(options.Duration);

            long totalBytes = 0;
            long chunks = 0;
            long randomAccessPoints = 0;
            try
            {
                await foreach (MediaStreamChunk chunk in session.ReadAllAsync(
                    durationCancellation.Token).ConfigureAwait(false))
                {
                    await output.WriteAsync(chunk.Data, CancellationToken.None).ConfigureAwait(false);
                    totalBytes += chunk.Data.Length;
                    chunks++;
                    if (chunk.StartsAtRandomAccessPoint)
                    {
                        randomAccessPoints++;
                    }
                }
            }
            catch (OperationCanceledException) when (durationCancellation.IsCancellationRequested)
            {
            }

            await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            MediaSessionStatistics statistics = session.GetStatistics();
            MediaEncoderDiagnostics encoder = session.GetEncoderDiagnostics();
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);

            bool canceledByUser = userCancellation.IsCancellationRequested;
            bool valid = !canceledByUser && totalBytes > 0 && statistics.EncodedVideoFrames > 0;
            WriteResult(new
            {
                status = canceledByUser ? "canceled" : valid ? "captured" : "empty",
                bytes = totalBytes,
                chunks,
                randomAccessPoints,
                statistics.CapturedVideoFrames,
                statistics.EncodedVideoFrames,
                statistics.DroppedVideoFrames,
                statistics.CapturedAudioPackets,
                statistics.EncodedAudioFrames,
                statistics.AudioDeviceChanges,
                statistics.QueueOverflows,
                statistics.TimestampCorrections,
                encoderWaitMilliseconds = statistics.EncoderWaitTime.TotalMilliseconds,
                encoder,
                durationSeconds = options.Duration.TotalSeconds,
                options.Width,
                options.Height,
                frameRate = 30,
                options.IncludeAudio,
                options.AudioBitrate,
                options.MuteLocalPlayback,
            });
            return canceledByUser ? 130 : valid ? 0 : 5;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException or NativeMediaException)
        {
            string message = exception.Message.Length <= 4096
                ? exception.Message
                : exception.Message[..4096];
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

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}

internal sealed record ProbeOptions(
    string OutputPath,
    TimeSpan Duration,
    int Width,
    int Height,
    int VideoBitrate,
    int AudioBitrate,
    bool IncludeCursor,
    bool IncludeAudio,
    bool MuteLocalPlayback,
    bool Overwrite,
    bool ShowHelp)
{
    public const string Usage =
        "Usage: DesktopDlnaCast.NativeCaptureProbe --output <capture.ts> " +
        "[--duration-seconds 10] [--width 1280] [--height 720] " +
        "[--video-bitrate 3000000] [--audio-bitrate 128000] " +
        "[--include-cursor true|false] [--include-audio true|false] " +
        "[--mute-local-playback true|false] [--overwrite]";

    public static ProbeOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Any(static value => value is "--help" or "-h"))
        {
            return new("", TimeSpan.Zero, 0, 0, 0, 0, false, false, false, false, true);
        }

        string? outputPath = null;
        int durationSeconds = 10;
        int width = 1280;
        int height = 720;
        int videoBitrate = 3_000_000;
        int audioBitrate = 128_000;
        bool includeCursor = true;
        bool includeAudio = true;
        bool muteLocalPlayback = false;
        bool overwrite = false;
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--output":
                    outputPath = NextValue(args, ref index, argument);
                    break;
                case "--duration-seconds":
                    durationSeconds = ParseInteger(NextValue(args, ref index, argument), argument);
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
                case "--mute-local-playback":
                    muteLocalPlayback = ParseBoolean(NextValue(args, ref index, argument), argument);
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'. {Usage}");
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException($"--output is required. {Usage}");
        }

        if (!outputPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--output must use the .ts extension.");
        }

        if (durationSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--duration-seconds must be between 1 and 600.");
        }

        ValidateEvenDimension(width, 2, 7680, "--width");
        ValidateEvenDimension(height, 2, 4320, "--height");
        if (videoBitrate is < 100_000 or > 100_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--video-bitrate must be between 100000 and 100000000.");
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

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (outputDirectory is null || !Directory.Exists(outputDirectory))
        {
            throw new ArgumentException("The output directory does not exist.");
        }

        if (!overwrite && File.Exists(fullOutputPath))
        {
            throw new IOException("The output file already exists; pass --overwrite to replace it.");
        }

        return new(
            fullOutputPath,
            TimeSpan.FromSeconds(durationSeconds),
            width,
            height,
            videoBitrate,
            audioBitrate,
            includeCursor,
            includeAudio,
            muteLocalPlayback,
            overwrite,
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
