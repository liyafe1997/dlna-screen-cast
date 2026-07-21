using System.Net;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Network;

public sealed class RouteNetworkInterfaceSelectorTests
{
    [Fact]
    public async Task SelectLocalAddressAsyncUsesWindowsRouteForLoopbackTestReceiver()
    {
        RouteNetworkInterfaceSelector selector = new(Options.Create(new StreamingOptions
        {
            AllowLoopbackForTests = true,
        }));

        IPAddress selected = await selector.SelectLocalAddressAsync(IPAddress.Loopback, CancellationToken.None);

        Assert.Equal(IPAddress.Loopback, selected);
    }

    [Fact]
    public async Task SelectLocalAddressAsyncRejectsLoopbackOutsideTests()
    {
        RouteNetworkInterfaceSelector selector = new(Options.Create(new StreamingOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            selector.SelectLocalAddressAsync(IPAddress.Loopback, CancellationToken.None));
    }
}

