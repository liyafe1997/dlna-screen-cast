using System.Net;
using System.Net.Sockets;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Streaming.Configuration;
using Microsoft.Extensions.Options;

namespace DesktopDlnaCast.Streaming.Network;

public sealed class RouteNetworkInterfaceSelector(IOptions<StreamingOptions> options)
    : INetworkInterfaceSelector
{
    public Task<IPAddress> SelectLocalAddressAsync(
        IPAddress rendererAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rendererAddress);
        cancellationToken.ThrowIfCancellationRequested();
        if (rendererAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("Only renderer IPv4 routes are supported.");
        }

        if (IPAddress.IsLoopback(rendererAddress) && !options.Value.AllowLoopbackForTests)
        {
            throw new InvalidOperationException("Loopback renderer addresses are not valid outside tests.");
        }

        using Socket routeProbe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        routeProbe.Connect(new IPEndPoint(rendererAddress, 9));
        if (routeProbe.LocalEndPoint is not IPEndPoint localEndPoint ||
            localEndPoint.Address.Equals(IPAddress.Any) ||
            (!options.Value.AllowLoopbackForTests && IPAddress.IsLoopback(localEndPoint.Address)))
        {
            throw new InvalidOperationException("Windows did not select a usable IPv4 route to the renderer.");
        }

        return Task.FromResult(localEndPoint.Address);
    }
}

