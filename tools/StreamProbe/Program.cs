using System.Net;
using System.Text.Json;

namespace DesktopDlnaCast.StreamProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            ProbeCommand command = ProbeCommand.Parse(args);
            using CancellationTokenSource timeout = new(command.Timeout);
            await using Stream source = await OpenSourceAsync(command.Input, timeout.Token).ConfigureAwait(false);
            StreamProbeResult result = await StreamProbeEngine.ProbeAsync(
                source,
                command.RequireAudio,
                command.MaximumBytes,
                command.MaximumGop,
                timeout.Token).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                status = "valid",
                source = command.Input.IsFile ? "file" : "http",
                result.BytesRead,
                result.PacketCount,
                result.PatSeen,
                result.PmtSeen,
                result.H264Seen,
                result.AacSeen,
                result.PtsMonotonic,
                result.VideoPtsCount,
                result.AudioPtsCount,
                result.IdrCount,
                result.MaximumIdrInterval90Khz,
                result.SpsSeen,
                result.PpsSeen,
            })).ConfigureAwait(false);
            return 0;
        }
        catch (ArgumentException exception)
        {
            await WriteErrorAsync("arguments", exception).ConfigureAwait(false);
            return 2;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await WriteErrorAsync("invalid", exception).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<Stream> OpenSourceAsync(Uri input, CancellationToken cancellationToken)
    {
        if (input.IsFile)
        {
            return new FileStream(
                input.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 32,
        };
        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        try
        {
            HttpResponseMessage response = await client.GetAsync(
                input,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Dispose();
                throw new HttpRequestException(
                    $"The stream endpoint returned HTTP {(int)response.StatusCode}.",
                    inner: null,
                    response.StatusCode);
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "video/mpeg", StringComparison.OrdinalIgnoreCase))
            {
                response.Dispose();
                throw new InvalidDataException($"The stream Content-Type was '{mediaType ?? "<missing>"}', not video/mpeg.");
            }

            Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new OwnedHttpStream(responseStream, response, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static Task WriteErrorAsync(string status, Exception exception)
    {
        string message = exception.Message.Length > 4096 ? exception.Message[..4096] : exception.Message;
        return Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            status,
            errorType = exception.GetType().Name,
            message,
        }));
    }

    private sealed record ProbeCommand(
        Uri Input,
        bool RequireAudio,
        long MaximumBytes,
        TimeSpan MaximumGop,
        TimeSpan Timeout)
    {
        public static ProbeCommand Parse(string[] args)
        {
            string? input = null;
            bool requireAudio = true;
            long maximumBytes = 64L * 1024 * 1024;
            int maximumGopMilliseconds = 1500;
            int timeoutSeconds = 15;
            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                string value = ReadValue(args, ref index, option);
                switch (option)
                {
                    case "--input":
                        input = value;
                        break;
                    case "--require-audio":
                        requireAudio = bool.Parse(value);
                        break;
                    case "--max-bytes":
                        maximumBytes = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "--maximum-gop-ms":
                        maximumGopMilliseconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "--timeout-seconds":
                        timeoutSeconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {option}");
                }
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("--input <file-or-http-url> is required.");
            }

            Uri inputUri = CreateInputUri(input);
            if (maximumBytes is < 188 or > 256L * 1024 * 1024 ||
                maximumGopMilliseconds is < 1 or > 10_000 ||
                timeoutSeconds is < 1 or > 300)
            {
                throw new ArgumentException("A numeric StreamProbe option is outside its supported range.");
            }

            return new(
                inputUri,
                requireAudio,
                maximumBytes,
                TimeSpan.FromMilliseconds(maximumGopMilliseconds),
                TimeSpan.FromSeconds(timeoutSeconds));
        }

        private static Uri CreateInputUri(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                if (uri.IsFile || uri.Scheme is "http" or "https")
                {
                    return uri;
                }

                throw new ArgumentException("StreamProbe accepts only files and HTTP(S) URLs.");
            }

            return new(Path.GetFullPath(value));
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            index++;
            return index < args.Length
                ? args[index]
                : throw new ArgumentException($"{option} requires a value.");
        }
    }

    private sealed class OwnedHttpStream(
        Stream inner,
        HttpResponseMessage response,
        HttpClient client) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                client.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            response.Dispose();
            client.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
