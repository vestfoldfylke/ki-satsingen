using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// A stopped turn used to throw away everything the model had said. People press
// stop because they have what they needed, so the answer was not merely lost on
// reload — it disappeared off the screen the moment the stream ended.
public sealed class PartialResponsePersistenceTests
{
    [Fact]
    public async Task A_stopped_turn_keeps_what_the_model_had_already_said()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal("halvferdig svar", AssistantMessage(harness).Content);
    }

    [Fact]
    public async Task A_stopped_turn_still_records_that_it_was_stopped()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal(ChatEventKind.Stopped, Assert.Single(harness.Repository.AppendedEvents).Kind);
    }

    // The partial carries this model's key, so reopening the chat names it as the
    // last model to answer. The live session has to agree: a switch that a stopped
    // turn already ran on is no longer pending — not only after a refresh.
    [Fact]
    public async Task A_stopped_turn_takes_a_pending_switch_into_effect()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("hei");
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        // Asserted first: PendingModel is also null when nothing has ever
        // answered, so without this the null below could mean the switch never
        // registered rather than that the stopped turn took it into effect.
        Assert.NotNull(harness.Session.PendingModel);

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Null(harness.Session.PendingModel);
    }

    // End to end, through the real fold: usage arrives as a streamed UsageContent,
    // not as a property someone set. PartialTurnTests proves Prune carries Usage
    // across; this proves there is any Usage to carry.
    [Fact]
    public async Task A_stopped_turn_persists_the_usage_the_provider_had_already_reported()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringWithUsageThenStalling("halvferdig svar", 900, 100, reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;

        var assistant = AssistantMessage(harness);
        Assert.Equal(900, assistant.InputTokens);
        Assert.Equal(100, assistant.OutputTokens);
    }

    // The user-facing half. The streamed text lives in the streaming view, which
    // ends with the turn, so unless the partial is committed to the transcript it
    // disappears from the screen the moment stop is pressed — saved or not.
    [Fact]
    public async Task A_stopped_turn_stays_on_screen()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        var turn = Assert.Single(harness.Session.Committed.OfType<AssistantTurnView>());
        Assert.Equal("halvferdig svar", string.Concat(turn.Parts.Select(part => part.Text)));
    }

    // A turn that broke mid-stream has the same half-answer on screen as one the
    // user stopped, and the same reason to keep it.
    [Fact]
    public async Task A_failed_turn_keeps_what_the_model_had_already_said()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("halvferdig svar", new InvalidOperationException("provider gave up"));

        await harness.Session.SendAsync("hei");

        Assert.Equal("halvferdig svar", AssistantMessage(harness).Content);
    }

    // Nothing was said, so there is nothing to keep — and an empty assistant
    // bubble in the transcript is worse than no bubble.
    [Fact]
    public async Task A_turn_stopped_before_the_model_said_anything_stores_no_message()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;

        Assert.DoesNotContain(harness.Repository.AppendedMessages, message => message.Role == ChatRole.Assistant.Value);
    }

    // Stored, an unanswered tool call would go back out with every later request
    // and the provider would reject all of them. Losing the partial is a lost
    // answer; keeping this one would be a chat that can never be used again.
    [Fact]
    public async Task A_turn_stopped_during_a_tool_call_stores_nothing_that_would_break_the_chat()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.CallingAToolThenStalling(reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;

        Assert.DoesNotContain(harness.Repository.AppendedMessages, message => message.Role == ChatRole.Assistant.Value);
    }

    private static async Task StopAfterAnswering(ChatSessionHarness harness, string text)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling(text, reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await sending;
    }

    private static StoredMessage AssistantMessage(ChatSessionHarness harness) =>
        harness.Repository.AppendedMessages.Single(message => message.Role == ChatRole.Assistant.Value);
}
