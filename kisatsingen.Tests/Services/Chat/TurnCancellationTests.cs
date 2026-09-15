using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The cancellation rules on their own, with no model, database or circuit in
// sight — which is the reason this was worth pulling out of ChatSession. What is
// under test is the part a reader of ChatSession has to take on trust: that
// "cancelled" and "cancelled by whom" are answered from state that survives
// races and disposal.
public sealed class TurnCancellationTests
{
    [Fact]
    public void A_turn_that_nobody_cancelled_is_not_cancelled()
    {
        using var turn = new TurnCancellation();

        Assert.False(turn.IsCancelled);
        Assert.False(turn.Token.IsCancellationRequested);
    }

    [Fact]
    public void A_user_stop_cancels_the_turn_without_calling_it_a_disconnect()
    {
        using var turn = new TurnCancellation();

        turn.CancelForUser();

        Assert.True(turn.IsCancelled);
        Assert.False(turn.IsDisconnect);
    }

    [Fact]
    public void A_lost_circuit_cancels_the_turn_and_is_reported_as_a_disconnect()
    {
        using var turn = new TurnCancellation();

        turn.CancelForDisconnect();

        Assert.True(turn.IsCancelled);
        Assert.True(turn.IsDisconnect);
    }

    // Pressing stop on a dying tab is functionally a disconnect, so the answer
    // must not depend on which of the two happened to be observed first.
    [Fact]
    public void A_disconnect_outranks_a_user_stop_that_arrived_first()
    {
        using var turn = new TurnCancellation();

        turn.CancelForUser();
        turn.CancelForDisconnect();

        Assert.True(turn.IsDisconnect);
    }

    [Fact]
    public void A_disconnect_outranks_a_user_stop_that_arrived_after_it()
    {
        using var turn = new TurnCancellation();

        turn.CancelForDisconnect();
        turn.CancelForUser();

        Assert.True(turn.IsDisconnect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_awaited_token_fires_whichever_source_was_cancelled(bool isDisconnect)
    {
        using var turn = new TurnCancellation();

        if (isDisconnect)
        {
            turn.CancelForDisconnect();
        }
        else
        {
            turn.CancelForUser();
        }

        Assert.True(turn.Token.IsCancellationRequested);
    }

    // The turn's own teardown disposes this while a stop or a dead circuit may
    // still be arriving on another thread. Both callers are notifications with
    // nothing left to do, so the right behaviour is to absorb them.
    [Fact]
    public void Cancelling_after_disposal_is_absorbed_rather_than_thrown()
    {
        var turn = new TurnCancellation();
        turn.Dispose();

        var stop = Record.Exception(turn.CancelForUser);
        var disconnect = Record.Exception(turn.CancelForDisconnect);

        Assert.Null(stop);
        Assert.Null(disconnect);
    }

    [Fact]
    public async Task Cancelling_asynchronously_after_disposal_is_absorbed_rather_than_thrown()
    {
        var turn = new TurnCancellation();
        turn.Dispose();

        var failure = await Record.ExceptionAsync(turn.CancelForDisconnectAsync);

        Assert.Null(failure);
    }

    // ChatSession reads the reason out of this object inside a catch block that
    // races its own finally. If disposal erased the answer, a cancelled turn
    // would be recorded under the wrong name exactly when teardown was quickest.
    [Fact]
    public void Why_a_turn_ended_is_still_readable_after_disposal()
    {
        var turn = new TurnCancellation();
        turn.CancelForDisconnect();

        turn.Dispose();

        Assert.True(turn.IsCancelled);
        Assert.True(turn.IsDisconnect);
    }
}
