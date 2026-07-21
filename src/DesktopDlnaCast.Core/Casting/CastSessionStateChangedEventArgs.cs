namespace DesktopDlnaCast.Core.Casting;

public sealed class CastSessionStateChangedEventArgs(
    CastSessionState previousState,
    CastSessionState currentState) : EventArgs
{
    public CastSessionState PreviousState { get; } = previousState;

    public CastSessionState CurrentState { get; } = currentState;
}

