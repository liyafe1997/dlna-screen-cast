using System.Globalization;
using System.Net;
using System.Text.Json;

namespace DesktopDlnaCast.MockRenderer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        MockRendererOptions options;
        try
        {
            options = ParseOptions(args);
        }
        catch (ArgumentException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 2;
        }

        await using MockRendererHost host = new(options);
        using CancellationTokenSource shutdown = new();
        host.ShutdownRequested += (_, _) => shutdown.Cancel();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            await host.StartAsync(shutdown.Token).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                eventType = "ready",
                http = host.BaseUri,
                ssdp = host.SsdpEndPoint?.ToString(),
                udn = options.Udn,
            })).ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }

        return 0;
    }

    private static MockRendererOptions ParseOptions(string[] args)
    {
        MockRendererOptions options = new();
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--http-port":
                    options.HttpPort = ParseInt32(ReadValue(args, ref index), argument);
                    break;
                case "--ssdp-port":
                    options.SsdpPort = ParseInt32(ReadValue(args, ref index), argument);
                    break;
                case "--listen-address":
                    options.ListenAddress = IPAddress.Parse(ReadValue(args, ref index));
                    break;
                case "--allow-non-loopback":
                    options.AllowNonLoopback = true;
                    break;
                case "--method":
                    options.RequestMethod = Enum.Parse<MockRendererRequestMethod>(
                        ReadValue(args, ref index),
                        ignoreCase: true);
                    break;
                case "--pull-delay-ms":
                    options.PullDelay = TimeSpan.FromMilliseconds(ParseInt32(ReadValue(args, ref index), argument));
                    break;
                case "--disconnect-after-bytes":
                    options.DisconnectAfterBytes = ParseInt32(ReadValue(args, ref index), argument);
                    break;
                case "--reject-metadata":
                    options.RejectMetadata = true;
                    break;
                case "--fault-action":
                    options.FaultAction = ReadValue(args, ref index);
                    break;
                case "--forced-transport-state":
                    options.ForcedTransportState = ReadValue(args, ref index);
                    break;
                case "--header":
                    string header = ReadValue(args, ref index);
                    int separator = header.IndexOf(':');
                    if (separator <= 0 || !headers.TryAdd(header[..separator].Trim(), header[(separator + 1)..].Trim()))
                    {
                        throw new ArgumentException("--header requires a unique 'Name: Value' pair.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown MockRenderer argument: {argument}");
            }
        }

        options.RequestHeaders = headers;
        return options;
    }

    private static string ReadValue(string[] args, ref int index)
    {
        index++;
        return index < args.Length
            ? args[index]
            : throw new ArgumentException("A command-line option is missing its value.");
    }

    private static int ParseInt32(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new ArgumentException($"{option} requires an integer value.");
}
