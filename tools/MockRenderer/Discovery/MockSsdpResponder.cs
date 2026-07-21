using System.Net;
using System.Net.Sockets;
using System.Text;
using DesktopDlnaCast.MockRenderer.Diagnostics;

namespace DesktopDlnaCast.MockRenderer.Discovery;

internal sealed class MockSsdpResponder(
    IPAddress listenAddress,
    int port,
    string udn,
    Uri descriptionUri,
    MockRendererEventStore events) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Socket? socket;
    private Task? receiveTask;

    public IPEndPoint EndPoint { get; private set; } = new(listenAddress, port);

    public void Start()
    {
        if (socket is not null)
        {
            return;
        }

        Socket startedSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            startedSocket.Bind(new IPEndPoint(listenAddress, port));
            EndPoint = (IPEndPoint)startedSocket.LocalEndPoint!;
            socket = startedSocket;
            receiveTask = ReceiveLoopAsync(startedSocket, lifetime.Token);
            events.Record("SsdpReady", ("endpoint", EndPoint.ToString()));
        }
        catch
        {
            startedSocket.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        socket?.Dispose();
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (lifetime.IsCancellationRequested)
            {
            }
        }

        lifetime.Dispose();
    }

    private async Task ReceiveLoopAsync(Socket activeSocket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult received = await activeSocket.ReceiveFromAsync(
                buffer.AsMemory(),
                SocketFlags.None,
                remote,
                cancellationToken).ConfigureAwait(false);
            if (received.RemoteEndPoint is not IPEndPoint sender || received.ReceivedBytes == 0)
            {
                continue;
            }

            string request = Encoding.ASCII.GetString(buffer, 0, received.ReceivedBytes);
            if (!TryReadSearchTarget(request, out string? searchTarget))
            {
                continue;
            }

            string responseTarget = searchTarget.Equals("ssdp:all", StringComparison.OrdinalIgnoreCase)
                ? "urn:schemas-upnp-org:device:MediaRenderer:1"
                : searchTarget;
            string response = CreateResponse(responseTarget);
            await activeSocket.SendToAsync(
                Encoding.ASCII.GetBytes(response),
                SocketFlags.None,
                sender,
                cancellationToken).ConfigureAwait(false);
            events.Record(
                "SsdpSearchReceived",
                ("searchTarget", searchTarget),
                ("remote", sender.ToString()));
        }
    }

    private string CreateResponse(string searchTarget) =>
        "HTTP/1.1 200 OK\r\n" +
        "CACHE-CONTROL: max-age=120\r\n" +
        $"LOCATION: {descriptionUri.AbsoluteUri}\r\n" +
        "SERVER: Windows/11 UPnP/1.1 DesktopDlnaCast-MockRenderer/1.0\r\n" +
        $"ST: {searchTarget}\r\n" +
        $"USN: {udn}::{searchTarget}\r\n\r\n";

    private static bool TryReadSearchTarget(string request, out string searchTarget)
    {
        searchTarget = string.Empty;
        if (request.Length > 8192 ||
            !request.StartsWith("M-SEARCH * HTTP/1.1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string line in request.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.StartsWith("ST:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[3..].Trim();
            if (value.Equals("ssdp:all", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("urn:schemas-upnp-org:device:MediaRenderer:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("urn:schemas-upnp-org:service:AVTransport:", StringComparison.OrdinalIgnoreCase))
            {
                searchTarget = value;
                return true;
            }
        }

        return false;
    }
}

