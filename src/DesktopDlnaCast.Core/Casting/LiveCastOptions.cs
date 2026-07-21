namespace DesktopDlnaCast.Core.Casting;

public sealed class LiveCastOptions
{
    public const string SectionName = "LiveCast";

    public TimeSpan StartPointTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan PlaybackConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TransportPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan TransportMonitorInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan TransportMonitorCallTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int TransportMonitorStoppedThreshold { get; set; } = 2;

    public int TransportMonitorFailureThreshold { get; set; } = 3;
}
