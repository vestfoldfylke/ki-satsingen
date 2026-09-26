using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// People press stop when they have what they need, so the answer must survive it.
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

    // Through the real fold, from a streamed UsageContent rather than a property set by hand.
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

    // The streaming view ends with the turn; uncommitted, the text vanishes on stop.
    [Fact]
    public async Task A_stopped_turn_stays_on_screen()
    {
        await using var harness = new ChatSessionHarness();

        await StopAfterAnswering(harness, "halvferdig svar");

        var turn = Assert.Single(harness.Session.Committed.OfType<AssistantTurnView>());
        Assert.Equal("halvferdig svar", string.Concat(turn.Parts.Select(part => part.Text)));
    }

    [Fact]
    public async Task A_failed_turn_keeps_what_the_model_had_already_said()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("halvferdig svar", new InvalidOperationException("provider gave up"));

        await harness.Session.SendAsync("hei");

        Assert.Equal("halvferdig svar", AssistantMessage(harness).Content);
    }

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

    // Proves PartialTurn is actually wired into the stop path.
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

    // LoadAsync picks the model from this key, so a partial must carry it too.
    [Fact]
    public async Task A_stopped_turn_records_the_model_key_it_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await StopAfterAnswering(harness, "halvferdig svar");

        Assert.Equal(FakeChatModelCatalog.AlternativeKey.Value, AssistantMessage(harness).ModelKey);
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
