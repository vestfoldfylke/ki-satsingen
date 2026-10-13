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
    public async Task A_completed_turn_writes_the_counts_the_provider_reported()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 120, outputTokens: 45);

        await harness.Session.SendAsync("hei");

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(120, insert.InputTokens);
        Assert.Equal(45, insert.OutputTokens);
        Assert.Null(insert.EstimatedInputTokens);
        Assert.Null(insert.EstimatedOutputTokens);
        Assert.Equal(TurnStatus.Completed, insert.Status);
    }

    [Fact]
    public async Task A_completed_turn_records_the_model_it_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);
        var expected = harness.Catalog.Default;

        await harness.Session.SendAsync("hei");

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(expected.Provider, insert.Provider);
        Assert.Equal(expected.ModelId, insert.ModelId);
    }

    [Fact]
    public async Task The_row_is_owned_by_the_signed_in_caller()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(ChatSessionHarness.OwnerUnderTest, insert.OwnerId);
    }

    // A stop before the provider reported anything still costs: the request was sent.
    [Fact]
    public async Task A_stopped_turn_writes_an_estimate_when_the_provider_never_reported()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig", reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await send;

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(TurnStatus.Stopped, insert.Status);
        Assert.Null(insert.InputTokens);
        Assert.Null(insert.OutputTokens);
        Assert.NotNull(insert.EstimatedInputTokens);
    }

    // A round the provider did report keeps its reported counts and adds no
    // estimate for the same round, so the two are not double-counted.
    [Fact]
    public async Task A_stopped_turn_writes_reported_usage_as_is_when_the_provider_reported_it()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringWithUsageThenStalling(
            "halvferdig", inputTokens: 200, outputTokens: 50, reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.Cancel();
        await send;

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(200, insert.InputTokens);
        Assert.Equal(50, insert.OutputTokens);
        Assert.Null(insert.EstimatedInputTokens);
        Assert.Null(insert.EstimatedOutputTokens);
    }

    [Fact]
    public async Task A_failed_turn_writes_a_row_with_the_failed_status()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.FailingAfter("halvferdig", new InvalidOperationException("provider exploded"));

        await harness.Session.SendAsync("hei");

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(TurnStatus.Failed, insert.Status);
    }

    [Fact]
    public async Task A_disconnected_turn_writes_a_row_with_the_disconnected_status()
    {
        await using var harness = new ChatSessionHarness();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.Stalling(reached, ct);

        var send = harness.Session.SendAsync("hei");
        await reached.Task;
        harness.Session.CancelForDisconnect();
        await send;

        var insert = Assert.Single(harness.UserTokenUsageRepository.Inserts);
        Assert.Equal(TurnStatus.Disconnected, insert.Status);
    }

    // A turn whose metadata never carries a usage — nothing sent, nothing to store.
    [Fact]
    public async Task A_turn_the_model_was_never_asked_writes_no_token_usage()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.UserTokenUsageRepository.Inserts);
    }

    // Unauthenticated: rethrown, so the composer's finally never reaches the store.
    [Fact]
    public async Task An_unauthenticated_attempt_writes_no_token_usage()
    {
        await using var harness = new ChatSessionHarness();
        harness.Authentication.Failure = new UserNotAuthenticatedException();

        await Assert.ThrowsAsync<UserNotAuthenticatedException>(() => harness.Session.SendAsync("hei"));

        Assert.Empty(harness.UserTokenUsageRepository.Inserts);
    }

    // The store is a side channel: its outage cannot take the turn with it.
    [Fact]
    public async Task A_store_failure_does_not_escape_the_send()
    {
        await using var harness = new ChatSessionHarness();
        harness.UserTokenUsageRepository.InsertFailure = new InvalidOperationException("usage store away");

        var failure = await Record.ExceptionAsync(() => harness.Session.SendAsync("hei"));

        Assert.Null(failure);
        Assert.Equal(TurnStatus.Completed, harness.StoredTurn.Status);
    }

    // Sanity for the metric-versus-store reconciliation: one send, one row.
    [Fact]
    public async Task A_completed_turn_writes_exactly_one_token_usage_row()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");

        Assert.Single(harness.UserTokenUsageRepository.Inserts);
    }

    [Fact]
    public async Task Two_turns_in_a_row_write_two_token_usage_rows()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("svar", inputTokens: 1, outputTokens: 1);

        await harness.Session.SendAsync("hei");
        await harness.Session.SendAsync("igjen");

        Assert.Equal(2, harness.UserTokenUsageRepository.Inserts.Count);
    }
}
