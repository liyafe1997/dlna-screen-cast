namespace DesktopDlnaCast.Core.Casting;

public sealed class StaticMediaTestOptions
{
    public const string SectionName = "StaticMediaTest";

    public TimeSpan PlaybackConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TransportPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
