using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionOutcomeTests
{
    // The bug this exists for: a provider's HTTP timeout surfaces as
    // TaskCanceledException. Read as a cancellation, an outage is filed as a user stop.
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

    // Retried, the row would hold the whole answer under a notice saying it was not stored.
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

    // Exception messages carry connection details and provider error bodies.
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

    // Storing the ending can fail for the same reason the turn did, and must not
    // replace the outcome it was recording.
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

    // Nothing can be stored without a chat row, but silence would leave a question
    // with no answer and no reason.
    [Fact]
    public async Task A_failure_before_the_chat_exists_still_shows_the_user_a_notice()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.CreateChatFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Equal(TurnStatus.Failed, harness.VisibleTurn.Status);
        Assert.Empty(harness.Repository.Writes);
    }

    // Let out on purpose: it ends the circuit, and the reload that follows is
    // what the sign-in middleware redirects.
    [Fact]
    public async Task An_unauthenticated_caller_is_not_swallowed_as_a_chat_problem()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));
    }

    // Recorded as a chat problem, an allocation failure would invite a retry that
    // fails the same way. Nothing but this test stops the clause being removed.
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

    // Circuit teardown disposes the session under a running turn: an eviction or
    // shutdown, so a disconnect.
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

    [Fact]
    public async Task A_stop_during_the_final_save_stores_the_answer_as_completed()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.BeforeFirstUpdate = () => harness.Session.Cancel();

        await harness.Session.SendAsync("hei");

        Assert.Equal(TurnStatus.Completed, harness.StoredTurn.Status);
    }

    // It would open empty, with nothing to say why.
    [Fact]
    public async Task A_new_chat_whose_first_question_cannot_be_saved_is_deleted()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Single(harness.Repository.DeletedChatIds);
    }

    // Announced, the URL would name a chat that no longer exists.
    [Fact]
    public async Task A_new_chat_whose_first_question_cannot_be_saved_is_never_announced()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");
        var announcements = 0;
        harness.Session.ChatCreated += _ => announcements++;

        await harness.Session.SendAsync("hei");

        Assert.Equal((0, (Guid?)null), (announcements, harness.Session.ChatId));
    }

    [Fact]
    public async Task An_existing_chat_is_kept_when_a_question_cannot_be_saved()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(new kisatsingen.Data.Entities.Chat
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "stored"
        });
        await harness.Session.LoadAsync(chat.Id);
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.Repository.DeletedChatIds);
    }

    // A content filter or token limit can end a turn without a word.
    [Fact]
    public async Task A_turn_that_completes_without_saying_anything_says_so()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage(string.Empty, inputTokens: 10, outputTokens: 0);

        await harness.Session.SendAsync("hei");

        Assert.Equal("Modellen ga ikke noe svar", harness.VisibleNotice);
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

    // So a test cancels a turn genuinely in flight, not one already finished.
    private static Task InFlight(ChatSessionHarness harness)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);
        return reached.Task;
    }
}
