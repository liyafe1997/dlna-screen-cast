namespace DesktopDlnaCast.Core.Casting;

public sealed class InvalidCastStateTransitionException(
    CastSessionState from,
    CastSessionState to)
    : InvalidOperationException($"The cast session cannot transition from {from} to {to}.")
{
    public CastSessionState From { get; } = from;

    public CastSessionState To { get; } = to;
}

