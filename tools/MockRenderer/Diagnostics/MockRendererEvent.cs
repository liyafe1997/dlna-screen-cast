namespace DesktopDlnaCast.MockRenderer.Diagnostics;

public sealed record MockRendererEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Type,
    IReadOnlyDictionary<string, string> Data);

