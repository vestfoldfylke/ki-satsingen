using kisatsingen.Data.Entities;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// One row per ended attempt: the store must land beside the metric, or reconciling
// spend against traffic pulls apart. The row carries how the attempt ended so a
// failed round trip is billed but readable as such.
public sealed class ChatSessionTokenUsageTests
{
    [Fact]
    public async Task A_completed_turn_records_the_counts_the_provider_reported()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 120, outputTokens: 45, cachedInputTokens: 80);

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(new MessageUsage(120, 45, CachedInputTokens: 80), entry.Usage);
        Assert.Equal(TurnStatus.Completed, entry.Status);
    }

    [Fact]
    public async Task A_completed_turn_records_the_model_it_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);
        var expected = harness.Catalog.Default;

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(expected.Provider, entry.Provider);
        Assert.Equal(expected.ModelId, entry.ModelId);
    }

    [Fact]
    public async Task The_entry_is_owned_by_the_signed_in_caller()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(ChatSessionHarness.OwnerUnderTest, entry.OwnerId);
    }

    [Fact]
    public async Task Every_turn_in_a_chat_records_that_chat()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");
        await harness.Session.SendAsync("igjen");

        var chatId = Assert.Single(harness.Repository.CreatedChatIds);
        Assert.Equal([chatId, chatId], harness.UserTokenUsageRepository.Entries.Select(entry => entry.ChatId));
    }

    [Fact]
    public async Task The_entry_names_the_turn_it_was_spent_on()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(harness.StoredTurn.Id, entry.TurnId);
    }

    [Fact]
    public async Task A_turn_in_an_assistant_chat_records_the_assistant()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);
        var assistant = new Assistant
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Name = "Saksbehandler",
            Instructions = "Svar kort.",
            Visibility = AssistantVisibility.Private
        };
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: []);

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(assistant.Id, entry.AssistantId);
    }

    [Fact]
    public async Task A_turn_in_a_plain_chat_records_no_assistant()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Null(entry.AssistantId);
    }

    // A stop before the provider reported anything still costs: the request was sent.
    [Fact]
    public async Task A_stopped_turn_records_an_estimate_when_the_provider_never_reported()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig", reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await send;

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(TurnStatus.Stopped, entry.Status);
        Assert.Null(entry.Usage.InputTokens);
        Assert.Null(entry.Usage.OutputTokens);
        Assert.NotNull(entry.Usage.EstimatedInputTokens);
    }

    // A round the provider did report keeps its reported counts and adds no
    // estimate for the same round, so the two are not double-counted.
    [Fact]
    public async Task A_stopped_turn_records_reported_usage_as_is_when_the_provider_reported_it()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringWithUsageThenStalling(
            "halvferdig", inputTokens: 200, outputTokens: 50, reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await send;

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(new MessageUsage(200, 50), entry.Usage);
    }

    [Fact]
    public async Task A_failed_turn_records_an_entry_with_the_failed_status()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("halvferdig", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(TurnStatus.Failed, entry.Status);
    }

    [Fact]
    public async Task A_disconnected_turn_records_an_entry_with_the_disconnected_status()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.CancelForDisconnect();
        await send;

        var entry = Assert.Single(harness.UserTokenUsageRepository.Entries);
        Assert.Equal(TurnStatus.Disconnected, entry.Status);
    }

    // A turn whose metadata never carries a usage — nothing sent, nothing to record.
    [Fact]
    public async Task A_turn_the_model_was_never_asked_records_no_token_usage()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.UserTokenUsageRepository.Entries);
    }

    // Unauthenticated: rethrown, so the composer's finally never reaches the usage store.
    [Fact]
    public async Task An_unauthenticated_attempt_records_no_token_usage()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));

        Assert.Empty(harness.UserTokenUsageRepository.Entries);
    }

    // The usage store is a side channel: its outage cannot take the turn with it.
    [Fact]
    public async Task A_usage_store_failure_does_not_escape_the_send()
    {
        await using var harness = new ChatSessionHarness();
        harness.UserTokenUsageRepository.InsertFailure = new InvalidOperationException("usage store away");

        var failure = await Record.ExceptionAsync(() => harness.Session.SendAsync("hei"));

        Assert.Null(failure);
        Assert.Equal(TurnStatus.Completed, harness.StoredTurn.Status);
    }

    // Sanity for the metric-versus-store reconciliation: one send, one entry.
    [Fact]
    public async Task A_completed_turn_records_exactly_one_entry()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        Assert.Single(harness.UserTokenUsageRepository.Entries);
    }

    [Fact]
    public async Task Two_turns_in_a_row_record_two_entries()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");
        await harness.Session.SendAsync("igjen");

        Assert.Equal(2, harness.UserTokenUsageRepository.Entries.Count);
    }
}
