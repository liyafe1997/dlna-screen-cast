namespace DesktopDlnaCast.Upnp.Discovery;

public sealed class SsdpDiscoveryOptions
{
    public const string SectionName = "Ssdp";

    public static IReadOnlyList<string> DefaultSearchTargets { get; } =
    [
        "urn:schemas-upnp-org:device:MediaRenderer:1",
        "urn:schemas-upnp-org:service:AVTransport:1",
        "ssdp:all",
    ];

    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan DescriptionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaximumWaitSeconds { get; set; } = 2;

    public IReadOnlyList<string> SearchTargets { get; set; } = DefaultSearchTargets;
}
