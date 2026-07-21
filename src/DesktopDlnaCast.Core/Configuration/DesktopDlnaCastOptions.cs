namespace DesktopDlnaCast.Core.Configuration;

public sealed class DesktopDlnaCastOptions
{
    public const string SectionName = "DesktopDlnaCast";

    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaximumXmlResponseBytes { get; set; } = 1024 * 1024;

    public int MaximumDiagnosticTextLength { get; set; } = 16 * 1024;
}

