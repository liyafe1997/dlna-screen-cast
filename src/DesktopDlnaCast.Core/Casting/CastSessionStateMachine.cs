using Microsoft.Extensions.Logging;

namespace DesktopDlnaCast.Core.Casting;

public sealed class CastSessionStateMachine(ILogger<CastSessionStateMachine> logger)
{
    private static readonly Dictionary<CastSessionState, HashSet<CastSessionState>> AllowedTransitions =
        new Dictionary<CastSessionState, HashSet<CastSessionState>>
        {
            [CastSessionState.Idle] = [CastSessionState.Discovering],
            [CastSessionState.Discovering] = [CastSessionState.ProbingRenderer],
            [CastSessionState.ProbingRenderer] = [CastSessionState.StartingMediaPipeline],
            [CastSessionState.StartingMediaPipeline] = [CastSessionState.WaitingForKeyframe],
            [CastSessionState.WaitingForKeyframe] =
                [CastSessionState.Publishing, CastSessionState.StartingMediaPipeline],
            [CastSessionState.Publishing] =
                [CastSessionState.SendingTransportUri, CastSessionState.StartingMediaPipeline],
            [CastSessionState.SendingTransportUri] =
                [CastSessionState.StartingPlayback, CastSessionState.StartingMediaPipeline],
            [CastSessionState.StartingPlayback] =
                [CastSessionState.Playing, CastSessionState.StartingMediaPipeline],
            [CastSessionState.Playing] = [],
            [CastSessionState.Stopping] = [CastSessionState.Idle],
            [CastSessionState.Faulted] = [CastSessionState.Stopping],
        };

    private static readonly Action<ILogger, CastSessionState, CastSessionState, Exception?> LogStateChanged =
        LoggerMessage.Define<CastSessionState, CastSessionState>(
            LogLevel.Information,
            new EventId(1, nameof(LogStateChanged)),
            "Cast session state changed from {PreviousState} to {CurrentState}");

    private static readonly Action<ILogger, CastSessionState, Exception?> LogFaulted =
        LoggerMessage.Define<CastSessionState>(
            LogLevel.Error,
            new EventId(2, nameof(LogFaulted)),
            "Cast session entered the faulted state from {PreviousState}");

    private readonly object sync = new();
    private CastSessionState state = CastSessionState.Idle;

    public event EventHandler<CastSessionStateChangedEventArgs>? StateChanged;

    public CastSessionState State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public bool TryTransition(CastSessionState nextState)
    {
        CastSessionState previousState;
        lock (sync)
        {
            previousState = state;
            if (previousState == nextState)
            {
                return true;
            }

            if (!IsAllowed(previousState, nextState))
            {
                return false;
            }

            state = nextState;
        }

        LogStateChanged(logger, previousState, nextState, null);
        StateChanged?.Invoke(this, new(previousState, nextState));
        return true;
    }

    public void Transition(CastSessionState nextState)
    {
        CastSessionState previousState = State;
        if (!TryTransition(nextState))
        {
            throw new InvalidCastStateTransitionException(previousState, nextState);
        }
    }

    public bool TryBeginStopping()
    {
        CastSessionState previousState;
        lock (sync)
        {
            previousState = state;
            if (previousState is CastSessionState.Idle or CastSessionState.Stopping)
            {
                return false;
            }

            state = CastSessionState.Stopping;
        }

        LogStateChanged(logger, previousState, CastSessionState.Stopping, null);
        StateChanged?.Invoke(this, new(previousState, CastSessionState.Stopping));
        return true;
    }

    public bool TryFault()
    {
        CastSessionState previousState;
        lock (sync)
        {
            previousState = state;
            if (previousState is CastSessionState.Idle or CastSessionState.Stopping or CastSessionState.Faulted)
            {
                return false;
            }

            state = CastSessionState.Faulted;
        }

        LogFaulted(logger, previousState, null);
        StateChanged?.Invoke(this, new(previousState, CastSessionState.Faulted));
        return true;
    }

    private static bool IsAllowed(CastSessionState from, CastSessionState to) =>
        AllowedTransitions[from].Contains(to);
}
