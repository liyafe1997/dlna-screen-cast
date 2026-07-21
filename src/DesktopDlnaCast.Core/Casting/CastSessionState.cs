namespace DesktopDlnaCast.Core.Casting;

public enum CastSessionState
{
    Idle,
    Discovering,
    ProbingRenderer,
    StartingMediaPipeline,
    WaitingForKeyframe,
    Publishing,
    SendingTransportUri,
    StartingPlayback,
    Playing,
    Stopping,
    Faulted,
}

