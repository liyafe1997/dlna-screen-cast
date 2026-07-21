using DesktopDlnaCast.Core.Casting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopDlnaCast.Core.Tests.Casting;

public sealed class CastSessionStateMachineTests
{
    [Fact]
    public void TransitionFollowsRequiredHappyPath()
    {
        CastSessionStateMachine machine = CreateMachine();
        CastSessionState[] states =
        [
            CastSessionState.Discovering,
            CastSessionState.ProbingRenderer,
            CastSessionState.StartingMediaPipeline,
            CastSessionState.WaitingForKeyframe,
            CastSessionState.Publishing,
            CastSessionState.SendingTransportUri,
            CastSessionState.StartingPlayback,
            CastSessionState.Playing,
        ];

        foreach (CastSessionState state in states)
        {
            machine.Transition(state);
        }

        Assert.Equal(CastSessionState.Playing, machine.State);
    }

    [Fact]
    public void TransitionRejectsOutOfOrderState()
    {
        CastSessionStateMachine machine = CreateMachine();

        InvalidCastStateTransitionException exception = Assert.Throws<InvalidCastStateTransitionException>(
            () => machine.Transition(CastSessionState.Playing));

        Assert.Equal(CastSessionState.Idle, exception.From);
        Assert.Equal(CastSessionState.Playing, exception.To);
        Assert.Equal(CastSessionState.Idle, machine.State);
    }

    [Fact]
    public void StopIsIdempotentAndReturnsToIdle()
    {
        CastSessionStateMachine machine = CreateMachine();
        machine.Transition(CastSessionState.Discovering);

        Assert.True(machine.TryBeginStopping());
        Assert.False(machine.TryBeginStopping());
        machine.Transition(CastSessionState.Idle);
        Assert.False(machine.TryBeginStopping());

        Assert.Equal(CastSessionState.Idle, machine.State);
    }

    [Fact]
    public void StateChangedIsRaisedAfterSuccessfulTransition()
    {
        CastSessionStateMachine machine = CreateMachine();
        CastSessionStateChangedEventArgs? observed = null;
        machine.StateChanged += (_, args) => observed = args;

        machine.Transition(CastSessionState.Discovering);

        Assert.NotNull(observed);
        Assert.Equal(CastSessionState.Idle, observed.PreviousState);
        Assert.Equal(CastSessionState.Discovering, observed.CurrentState);
    }

    private static CastSessionStateMachine CreateMachine() => new(NullLogger<CastSessionStateMachine>.Instance);
}
