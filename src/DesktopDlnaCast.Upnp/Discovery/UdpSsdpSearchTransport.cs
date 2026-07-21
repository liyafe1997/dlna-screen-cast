using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed class UdpSsdpSearchTransport : ISsdpSearchTransport
{
    private static readonly IPEndPoint DefaultMulticastEndPoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    private const int MaximumResponses = 256;
    private readonly IPEndPoint searchEndPoint;

    public UdpSsdpSearchTransport()
        : this(DefaultMulticastEndPoint)
    {
    }

    public UdpSsdpSearchTransport(IPEndPoint searchEndPoint)
    {
        ArgumentNullException.ThrowIfNull(searchEndPoint);
        if (searchEndPoint.AddressFamily != AddressFamily.InterNetwork || searchEndPoint.Port is < 1 or > 65535)
        {
            throw new ArgumentException("The SSDP search endpoint must be a concrete IPv4 endpoint.", nameof(searchEndPoint));
        }

        this.searchEndPoint = searchEndPoint;
    }

    public async Task<IReadOnlyList<SsdpDatagram>> SearchAsync(
        LanNetworkInterface networkInterface,
        string searchTarget,
        int maximumWaitSeconds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);
        ValidateArguments(searchTarget, maximumWaitSeconds, timeout);

        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (IsMulticast(searchEndPoint.Address))
        {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                networkInterface.Address.GetAddressBytes());
        }

        socket.Bind(new IPEndPoint(networkInterface.Address, 0));

        byte[] request = Encoding.ASCII.GetBytes(CreateRequest(searchTarget, maximumWaitSeconds, searchEndPoint));
        await socket.SendToAsync(request, SocketFlags.None, searchEndPoint, cancellationToken)
            .ConfigureAwait(false);

        List<SsdpDatagram> responses = [];
        byte[] buffer = new byte[SsdpResponseParser.MaximumDatagramBytes];
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            while (responses.Count < MaximumResponses)
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                SocketReceiveFromResult received = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    remoteEndPoint,
                    timeoutSource.Token).ConfigureAwait(false);
                if (received.ReceivedBytes == 0 || received.RemoteEndPoint is not IPEndPoint sender)
                {
                    continue;
                }

                responses.Add(new(
                    buffer.AsSpan(0, received.ReceivedBytes).ToArray(),
                    sender,
                    networkInterface.Address));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return responses.AsReadOnly();
    }

    public static string CreateRequest(string searchTarget, int maximumWaitSeconds)
        => CreateRequest(searchTarget, maximumWaitSeconds, DefaultMulticastEndPoint);

    private static string CreateRequest(
        string searchTarget,
        int maximumWaitSeconds,
        IPEndPoint endPoint)
    {
        ValidateSearchTarget(searchTarget);
        if (maximumWaitSeconds is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWaitSeconds));
        }

        return "M-SEARCH * HTTP/1.1\r\n" +
            $"HOST: {endPoint.Address}:{endPoint.Port}\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            $"MX: {maximumWaitSeconds}\r\n" +
            $"ST: {searchTarget}\r\n\r\n";
    }

    private static void ValidateArguments(string searchTarget, int maximumWaitSeconds, TimeSpan timeout)
    {
        ValidateSearchTarget(searchTarget);
        if (maximumWaitSeconds is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWaitSeconds));
        }

        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private static void ValidateSearchTarget(string searchTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchTarget);
        if (searchTarget.Length > 512 || searchTarget.Any(char.IsControl))
        {
            throw new ArgumentException("The SSDP search target is invalid.", nameof(searchTarget));
        }
    }

    private static bool IsMulticast(IPAddress address)
    {
        byte first = address.GetAddressBytes()[0];
        return first is >= 224 and <= 239;
    }
}
