using System.Net;
using System.Text;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Upnp.Description;
using DesktopDlnaCast.Upnp.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopDlnaCast.Upnp.Tests.Discovery;

public sealed class SsdpDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsyncSearchesEveryTargetOnEveryInterfaceAndDeduplicatesByUdn()
    {
        LanNetworkInterface[] interfaces =
        [
            new("ethernet", "Ethernet", IPAddress.Parse("192.168.1.10"), 4),
            new("wifi", "Wi-Fi", IPAddress.Parse("192.168.1.11"), 7),
        ];
        List<(string InterfaceId, string Target)> calls = [];
        TestSearchTransport transport = new((networkInterface, target, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add((networkInterface.Id, target));
            return Task.FromResult<IReadOnlyList<SsdpDatagram>>(
            [
                CreateDatagram("uuid:renderer-a", "a", "192.168.1.42", networkInterface.Address),
                CreateDatagram("UUID:RENDERER-A", "a", "192.168.1.42", networkInterface.Address),
                CreateDatagram("uuid:renderer-b", "b", "192.168.1.43", networkInterface.Address),
                new(Encoding.ASCII.GetBytes("not ssdp"), new(IPAddress.Loopback, 1900), networkInterface.Address),
            ]);
        });
        TestDescriptionClient descriptions = new(new Dictionary<string, RendererDeviceDescription>
        {
            ["/device-a.xml"] = CreateDescription("uuid:renderer-a", "Living Room"),
            ["/device-b.xml"] = CreateDescription("uuid:renderer-b", "Bedroom"),
        });
        SsdpDiscoveryService service = CreateService(interfaces, transport, descriptions);

        IReadOnlyList<RendererDevice> devices = await CollectAsync(service.DiscoverAsync(CancellationToken.None));

        Assert.Equal(interfaces.Length * SsdpDiscoveryOptions.DefaultSearchTargets.Count, calls.Count);
        Assert.Equal(2, devices.Count);
        Assert.Equal("Bedroom", devices[0].FriendlyName);
        Assert.Equal(IPAddress.Parse("192.168.1.43"), devices[0].Address);
        Assert.Equal("Living Room", devices[1].FriendlyName);
        Assert.Equal(2, descriptions.RequestCount);
    }

    [Fact]
    public async Task DiscoverAsyncPropagatesCallerCancellation()
    {
        LanNetworkInterface[] interfaces =
        [
            new("ethernet", "Ethernet", IPAddress.Parse("192.168.1.10"), 4),
        ];
        TestSearchTransport transport = new(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        });
        SsdpDiscoveryService service = CreateService(
            interfaces,
            transport,
            new TestDescriptionClient(new Dictionary<string, RendererDeviceDescription>()));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CollectAsync(service.DiscoverAsync(cancellation.Token)));
    }

    [Fact]
    public async Task DiscoverAsyncRejectsDescriptionWhoseUdnDoesNotMatchSsdp()
    {
        LanNetworkInterface networkInterface = new(
            "ethernet",
            "Ethernet",
            IPAddress.Parse("192.168.1.10"),
            4);
        TestSearchTransport transport = new((_, _, _) =>
            Task.FromResult<IReadOnlyList<SsdpDatagram>>(
            [
                CreateDatagram("uuid:ssdp-device", "mismatch", "192.168.1.42", networkInterface.Address),
            ]));
        TestDescriptionClient descriptions = new(new Dictionary<string, RendererDeviceDescription>
        {
            ["/device-mismatch.xml"] = CreateDescription("uuid:different-device", "Spoofed"),
        });
        SsdpDiscoveryService service = CreateService([networkInterface], transport, descriptions);

        IReadOnlyList<RendererDevice> devices = await CollectAsync(service.DiscoverAsync(CancellationToken.None));

        Assert.Empty(devices);
    }

    private static SsdpDiscoveryService CreateService(
        IReadOnlyList<LanNetworkInterface> interfaces,
        ISsdpSearchTransport transport,
        IDeviceDescriptionClient descriptionClient) =>
        new(
            new TestInterfaceProvider(interfaces),
            transport,
            descriptionClient,
            new()
            {
                SearchTimeout = TimeSpan.FromMilliseconds(100),
                DescriptionTimeout = TimeSpan.FromSeconds(1),
                MaximumWaitSeconds = 1,
            },
            NullLogger<SsdpDiscoveryService>.Instance);

    private static SsdpDatagram CreateDatagram(
        string udn,
        string devicePath,
        string senderAddress,
        IPAddress localAddress)
    {
        string response =
            "HTTP/1.1 200 OK\r\n" +
            $"LOCATION: http://{senderAddress}:1400/device-{devicePath}.xml\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n" +
            $"USN: {udn}::urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n";
        return new(
            Encoding.ASCII.GetBytes(response),
            new(IPAddress.Parse(senderAddress), 1900),
            localAddress);
    }

    private static RendererDeviceDescription CreateDescription(string udn, string friendlyName) =>
        new(
            udn,
            friendlyName,
            "DesktopDlnaCast",
            "Test Renderer",
            new("http://192.168.1.42:1400/device.xml"),
            []);

    private static async Task<IReadOnlyList<RendererDevice>> CollectAsync(
        IAsyncEnumerable<RendererDevice> source)
    {
        List<RendererDevice> result = [];
        await foreach (RendererDevice device in source)
        {
            result.Add(device);
        }

        return result;
    }

    private sealed class TestInterfaceProvider(IReadOnlyList<LanNetworkInterface> interfaces)
        : ILanNetworkInterfaceProvider
    {
        public IReadOnlyList<LanNetworkInterface> GetEligibleInterfaces() => interfaces;
    }

    private sealed class TestSearchTransport(
        Func<LanNetworkInterface, string, CancellationToken, Task<IReadOnlyList<SsdpDatagram>>> callback)
        : ISsdpSearchTransport
    {
        public Task<IReadOnlyList<SsdpDatagram>> SearchAsync(
            LanNetworkInterface networkInterface,
            string searchTarget,
            int maximumWaitSeconds,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            callback(networkInterface, searchTarget, cancellationToken);
    }

    private sealed class TestDescriptionClient(
        IReadOnlyDictionary<string, RendererDeviceDescription> descriptions)
        : IDeviceDescriptionClient
    {
        public int RequestCount { get; private set; }

        public Task<RendererDeviceDescription> GetAsync(
            Uri descriptionUri,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(descriptions[descriptionUri.AbsolutePath]);
        }
    }
}
