namespace DesktopDlnaCast.Core.Models;

public sealed record UserSettings
{
    public int OutputWidth { get; init; } = 1280;

    public int OutputHeight { get; init; } = 720;

    public bool IncludeCursor { get; init; } = true;

    public bool IncludeAudio { get; init; } = true;

    public bool AudioOnly { get; init; }

    public bool MuteLocalPlayback { get; init; }

    public int GopFrames { get; init; } = 30;

    public int VideoBitratePercent { get; init; } = 100;

    public bool StartAtLiveEdge { get; init; }

    public AspectRatioMode AspectRatioMode { get; init; } = AspectRatioMode.Letterbox;

    public string? RendererUdn { get; init; }

    public DisplayUserSettings? Display { get; init; }
}

public sealed record DisplayUserSettings(
    int Index,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);
