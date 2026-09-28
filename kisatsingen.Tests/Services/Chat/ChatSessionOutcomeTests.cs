using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// How a turn ends, and what the user is told about it afterwards — on the page
// now, and from the stored row on the next load.
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

        Assert.Equal(TurnStatus.Failed, harness.VisibleTurn.Status);
    }

    [Fact]
    public async Task A_turn_that_breaks_while_generating_says_the_answer_could_not_be_finished()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        Assert.Equal("Svaret kunne ikke fullføres", harness.VisibleNotice);
    }

    // The one failure the user cannot see for themselves: the answer is on their
    // screen, complete, and will be gone after a reload.
    [Fact]
    public async Task A_response_that_cannot_be_saved_says_it_was_shown_but_not_stored()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.FirstUpdateFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal("Svaret ble vist, men ikke lagret", harness.VisibleNotice);
    }

    // Retried, the row would hold the whole answer under a notice saying it was
    // not stored. Left as inserted, it reads back as unfinished — which is true.
    [Fact]
    public async Task A_response_that_cannot_be_saved_leaves_the_stored_turn_running()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.FirstUpdateFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal(TurnStatus.Running, harness.StoredTurn.Status);
    }

    [Fact]
    public async Task A_message_that_cannot_be_saved_says_so_before_the_model_is_ever_called()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal("Meldingen ble ikke lagret", harness.VisibleNotice);
        Assert.Empty(harness.Catalog.ResolvedKeys);
    }

    // Proves the rule is wired into the request, not just tested beside it.
    [Fact]
    public async Task A_message_that_cannot_be_saved_is_not_sent_with_the_next_one()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");
        await harness.Session.SendAsync("aldri lagret");
        harness.Repository.InsertTurnFailure = null;

        await harness.Session.SendAsync("prøv igjen");

        Assert.Equal(["prøv igjen"], harness.Client.LastMessages.Select(message => message.Text));
    }

    // Provider and database exceptions routinely carry connection details and
    // error bodies in their messages. Nothing derived from one may reach the page.
    [Fact]
    public async Task The_exception_message_never_reaches_the_transcript()
    {
        const string leakMarker = "internal detail the user must never see";

        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException(leakMarker));

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(leakMarker, harness.VisibleNotice);
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

    // Storing the ending is itself a database write, and it can fail for the
    // same reason the turn did. It must not replace the outcome it was recording.
    [Fact]
    public async Task A_failure_to_store_how_a_turn_ended_does_not_escape_the_turn()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));
        harness.Repository.FirstUpdateFailure = new InvalidOperationException("database still away");

        var failure = await Record.ExceptionAsync(() => harness.Session.SendAsync("hei"));

        Assert.Null(failure);
        Assert.Equal(TurnStatus.Failed, harness.VisibleTurn.Status);
    }

    // Nothing was stored and nothing can be — there is no chat row yet. Going
    // silent here would leave the user staring at a question that never got an answer.
    [Fact]
    public async Task A_failure_before_the_chat_exists_still_shows_the_user_a_notice()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.CreateChatFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal(TurnStatus.Failed, harness.VisibleTurn.Status);
        Assert.Empty(harness.Repository.Writes);
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

        Assert.Equal(TurnStatus.Running, harness.StoredTurn.Status);
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

        Assert.Equal(TurnStatus.Stopped, harness.VisibleTurn.Status);
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

        Assert.Equal(TurnStatus.Disconnected, harness.VisibleTurn.Status);
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

        Assert.Equal(TurnStatus.Disconnected, harness.VisibleTurn.Status);
    }

    // Circuit teardown disposes the session out from under a running turn. The
    // outcome still has to be attributable afterwards, and it is a disconnect —
    // the circuit was evicted after its reconnect period, the host is shutting down.
    [Fact]
    public async Task Disposing_the_session_mid_turn_is_recorded_as_a_disconnect()
    {
        var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        await harness.DisposeAsync();
        await send;

        Assert.Equal(TurnStatus.Disconnected, harness.StoredTurn.Status);
    }

    [Fact]
    public async Task A_stopped_turn_stores_that_it_was_stopped_for_the_next_load()
    {
        await using var harness = new ChatSessionHarness();
        var reached = InFlight(harness);

        var send = harness.Session.SendAsync("hei");
        await reached;
        harness.Session.Cancel();
        await send;

        Assert.Equal(TurnStatus.Stopped, harness.StoredTurn.Status);
    }

    [Fact]
    public async Task A_failed_turn_stores_the_stage_it_failed_at_for_the_next_load()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("Hei", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        Assert.Equal(TurnStatus.Failed, harness.StoredTurn.Status);
        Assert.Equal(TurnStage.Generating, harness.StoredTurn.FailedAt);
    }

    [Fact]
    public async Task A_turn_that_answers_shows_no_notice()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Null(harness.VisibleNotice);
        Assert.Equal(TurnStatus.Completed, harness.StoredTurn.Status);
    }

    // Proves the load wires its clock into the mapper, not just that the mapper has one.
    [Fact]
    public async Task Reopening_a_chat_whose_turn_never_finished_says_so()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(new kisatsingen.Data.Entities.Chat
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "stored",
            Turns =
            [
                new kisatsingen.Data.Entities.ChatTurn
                {
                    Id = Guid.NewGuid(),
                    StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
                    Prompt = "hei",
                    SystemPrompt = "be brief",
                    ModelKey = FakeChatModelCatalog.DefaultKey.Value,
                    Status = nameof(TurnStatus.Running),
                    AnswerJson = "[]"
                }
            ]
        });

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal("Svaret ble ikke fullført", harness.VisibleNotice);
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
