using System.Net;

namespace DesktopDlnaCast.MockRenderer;

public enum MockRendererRequestMethod
{
    Get,
    Head,
}

public sealed class MockRendererOptions
{
    public const string DefaultUdn = "uuid:desktop-dlna-cast-mock-renderer";

    public IPAddress ListenAddress { get; set; } = IPAddress.Loopback;

    public int HttpPort { get; set; }

    public int SsdpPort { get; set; }

    public bool AllowNonLoopback { get; set; }

    public string Udn { get; set; } = DefaultUdn;

    public string FriendlyName { get; set; } = "DesktopDlnaCast Mock Renderer";

    public MockRendererRequestMethod RequestMethod { get; set; } = MockRendererRequestMethod.Get;

    public TimeSpan PullDelay { get; set; }

    public int? DisconnectAfterBytes { get; set; }

    public bool RejectMetadata { get; set; }

    public string? FaultAction { get; set; }

    public string? ForcedTransportState { get; set; }

    public int MaximumPullBytes { get; set; } = 4 * 1024 * 1024;

    public bool RequireAudio { get; set; } = true;

    public IReadOnlyDictionary<string, string> RequestHeaders { get; set; } =
        new Dictionary<string, string>();
}
