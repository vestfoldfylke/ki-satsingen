using kisatsingen.Data.Entities;
using kisatsingen.Services;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// How a turn ends, and what the user is told about it afterwards. Every case
// here produces no answer, so the only thing distinguishing them is the event
// the session records — which is exactly what a reader of a reloaded transcript
// depends on being right.
public sealed class ChatSessionOutcomeTests
{
    // The bug this exists for: an HTTP timeout inside the provider client
    // surfaces as TaskCanceledException, which is an OperationCanceledException
    // nobody here asked for. Read as a cancellation, a real outage is filed as
    // the user pressing a button they never pressed.
    [Fact]
    public async Task A_provider_timeout_is_recorded_as_a_failure_rather_than_a_user_stop()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new TaskCanceledException());

        await harness.Session.SendAsync("hei");

        Assert.Equal(ChatEventKind.Failed, harness.SingleVisibleEvent.Kind);
    }

    [Fact]
    public async Task A_turn_that_breaks_while_generating_says_the_answer_could_not_be_finished()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        Assert.Equal("Svaret kunne ikke fullføres", harness.SingleVisibleEvent.Detail);
    }

    // The one failure the user cannot see for themselves: the answer is on their
    // screen, complete, and will be gone after a reload.
    [Fact]
    public async Task A_response_that_cannot_be_saved_says_it_was_shown_but_not_stored()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.SecondAppendMessagesFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal("Svaret ble vist, men ikke lagret", harness.SingleVisibleEvent.Detail);
    }

    [Fact]
    public async Task A_message_that_cannot_be_saved_says_so_before_the_model_is_ever_called()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.FirstAppendMessagesFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal("Meldingen ble ikke lagret", harness.SingleVisibleEvent.Detail);
    }

    // Provider and database exceptions routinely carry connection details and
    // error bodies in their messages. The notice is persisted and rendered, so
    // nothing derived from the exception may reach it.
    [Fact]
    public async Task The_exception_message_never_reaches_the_transcript()
    {
        const string leakMarker = "internal detail the user must never see";

        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException(leakMarker));

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(leakMarker, harness.SingleVisibleEvent.Detail);
    }

    [Fact]
    public async Task A_broken_turn_leaves_the_session_ready_for_the_next_one()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        var failure = await Record.ExceptionAsync(() => harness.Session.SendAsync("hei"));

        Assert.Null(failure);
        Assert.False(harness.Session.IsBusy);
    }

    // Recording the failure is itself a database write, and it can fail for the
    // same reason the turn did. It must not replace the outcome it was recording.
    [Fact]
    public async Task A_failure_to_persist_the_failure_does_not_escape_the_turn()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));
        harness.Repository.AppendEventFailure = new InvalidOperationException("database still away");

        var failure = await Record.ExceptionAsync(() => harness.Session.SendAsync("hei"));

        Assert.Null(failure);
        Assert.Equal(ChatEventKind.Failed, harness.SingleVisibleEvent.Kind);
    }

    // Nothing was persisted and nothing can be — there is no chat row yet and no
    // owner to attribute one to. Going silent here would leave the user staring
    // at a message that simply never got an answer.
    [Fact]
    public async Task A_failure_before_the_chat_exists_still_shows_the_user_a_notice()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.CreateChatFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal(ChatEventKind.Failed, harness.SingleVisibleEvent.Kind);
        Assert.Empty(harness.Repository.AppendedEvents);
    }

    [Fact]
    public async Task An_unauthenticated_caller_still_reaches_the_error_boundary()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));
    }

    // The other exception allowed out. An allocation failure says nothing about
    // this turn, and recording it as a chat problem invites a retry that fails
    // the same way. Nothing in the compiler stops someone removing that clause,
    // after which it would be swallowed like any other fault.
    [Fact]
    public async Task An_allocation_failure_is_not_swallowed_as_a_chat_problem()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new OutOfMemoryException());

        await Assert.ThrowsAsync<OutOfMemoryException>(() => harness.Session.SendAsync("hei"));

        Assert.Empty(harness.VisibleEvents);
    }

    [Fact]
    public async Task Pressing_stop_is_recorded_as_a_user_stop()
    {
        await using var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        await send;

        Assert.Equal(ChatEventKind.Stopped, harness.SingleVisibleEvent.Kind);
    }

    [Fact]
    public async Task A_lost_circuit_is_recorded_as_a_disconnect_rather_than_a_user_stop()
    {
        await using var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.CancelForDisconnect();
        await send;

        Assert.Equal(ChatEventKind.Disconnected, harness.SingleVisibleEvent.Kind);
    }

    [Fact]
    public async Task Stopping_a_turn_on_a_dying_tab_is_recorded_as_a_disconnect()
    {
        await using var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        harness.Session.CancelForDisconnect();
        await send;

        Assert.Equal(ChatEventKind.Disconnected, harness.SingleVisibleEvent.Kind);
    }

    // Circuit teardown disposes the session out from under a running turn. The
    // outcome still has to be attributable afterwards, and it is a disconnect —
    // the tab closed, the circuit was evicted, the host is shutting down.
    [Fact]
    public async Task Disposing_the_session_mid_turn_is_recorded_as_a_disconnect()
    {
        var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        await harness.DisposeAsync();
        await send;

        Assert.Equal(ChatEventKind.Disconnected, harness.SingleVisibleEvent.Kind);
    }

    [Fact]
    public async Task A_cancelled_turn_persists_its_reason_for_the_next_reload()
    {
        await using var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        await send;

        var persisted = Assert.Single(harness.Repository.AppendedEvents);
        Assert.Equal(ChatEventKind.Stopped, persisted.Kind);
    }

    // The in-memory notice is what the current page shows; this is what survives
    // to explain the gap on the next load, which is the whole point of the event.
    [Fact]
    public async Task A_failed_turn_persists_its_notice_for_the_next_reload()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        var persisted = Assert.Single(harness.Repository.AppendedEvents);
        Assert.Equal(ChatEventKind.Failed, persisted.Kind);
        Assert.Equal("Svaret kunne ikke fullføres", persisted.Detail);
    }

    [Fact]
    public async Task A_turn_that_answers_records_no_event_at_all()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.VisibleEvents);
        Assert.Empty(harness.Repository.AppendedEvents);
    }

    // Parks the model mid-turn and hands back a task that completes once the
    // stream is genuinely running, so a test cancels a turn in flight rather
    // than one that already finished.
    private static Task InFlight(ChatSessionHarness harness)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);
        return reached.Task;
    }
}
