using kisatsingen.Constants;
using kisatsingen.Services.Chat;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// Navigation mid-answer: the turn stays with its chat. A new chat has its id
// before it is saved, since a start page moves to the chat as it sends.
public sealed class ChatSessionNavigationTests
{
    // A turn that isn't stopped by navigation stalls forever; fail instead of hanging.
    private static readonly TimeSpan TurnWindDownLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Leaving_a_chat_mid_answer_saves_the_partial_answer_to_the_chat_it_was_asked_in()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        var askedIn = await LeaveMidAnswer(harness, other.Id);

        Assert.All(harness.Repository.Writes, write => Assert.Equal(askedIn, write.ChatId));
        Assert.Equal("halvferdig svar", TurnText.Of(harness.StoredTurn));
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_records_that_the_user_left_in_the_chat_it_was_asked_in()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        var askedIn = await LeaveMidAnswer(harness, other.Id);

        Assert.Equal(TurnStatus.LeftChat, harness.StoredTurn.Status);
        Assert.Equal(askedIn, harness.Repository.Writes[^1].ChatId);
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_counts_one_left_chat_outcome()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        await LeaveMidAnswer(harness, other.Id);

        var send = Assert.Single(harness.Metrics.Named(RecordingMetricsService.SendCounter));
        Assert.Equal(MetricConstants.MetricsResultLeftChatLabelValue, send.Label(MetricConstants.MetricsResultLabelName));
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_is_absent_from_the_failure_counter()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        await LeaveMidAnswer(harness, other.Id);

        Assert.Empty(harness.Metrics.Named(RecordingMetricsService.FailureCounter));
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_shows_nothing_of_it_in_the_chat_opened_next()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        await LeaveMidAnswer(harness, other.Id);

        Assert.Equal(other.Id, harness.Session.ChatId);
        Assert.Empty(harness.Session.Transcript);
    }

    // The page has moved to the chat's id before the chat is saved.
    [Fact]
    public async Task A_new_chat_is_saved_under_the_id_it_had_before_its_first_message()
    {
        await using var harness = new ChatSessionHarness();
        var idBeforeSending = harness.Session.ChatId;

        await harness.Session.SendAsync("hei");

        Assert.Equal(idBeforeSending, harness.Repository.Writes[^1].ChatId);
    }

    // What lets a start page keep a session that already is a new chat, and
    // replace one that has anything in it.
    [Fact]
    public async Task A_first_message_that_could_not_be_saved_still_ends_the_new_chat()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.InsertTurnFailure = new InvalidOperationException("database away");

        await harness.Session.SendAsync("hei");

        Assert.False(harness.Session.IsNewChat);
    }

    [Fact]
    public async Task A_chat_created_after_the_user_left_is_not_shown()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);
        var creating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var createReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Repository.BeforeCreateChat = async () =>
        {
            creating.SetResult();
            await createReleased.Task;
        };
        var sending = harness.Session.SendAsync("hei");
        await creating.Task.WaitAsync(TurnWindDownLimit);
        var leaving = harness.Session.OpenChatAsync(other.Id);
        createReleased.SetResult();
        await leaving.WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        Assert.Equal(other.Id, harness.Session.ChatId);
    }

    [Fact]
    public async Task A_message_in_an_existing_chat_creates_no_chat()
    {
        await using var harness = new ChatSessionHarness();
        var existing = StoreEmptyChat(harness);
        await harness.Session.OpenChatAsync(existing.Id);
        var creates = 0;
        harness.Repository.BeforeCreateChat = () =>
        {
            creates++;
            return Task.CompletedTask;
        };

        await harness.Session.SendAsync("hei");

        Assert.Equal(0, creates);
    }

    [Fact]
    public async Task Starting_a_new_chat_mid_answer_stops_it_as_leaving_the_chat()
    {
        await using var harness = new ChatSessionHarness();
        var (sending, askedIn) = await StartStalledAnswer(harness);

        await harness.Session.StartNewChatAsync().WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        Assert.Equal(TurnStatus.LeftChat, harness.StoredTurn.Status);
        Assert.Equal(askedIn, harness.Repository.Writes[^1].ChatId);
        Assert.True(harness.Session.IsNewChat);
    }

    // Checked before awaiting the send: once it has returned, IsBusy is false anyway.
    [Fact]
    public async Task Opening_another_chat_returns_only_once_the_answer_has_wound_down()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);
        var (sending, _) = await StartStalledAnswer(harness);

        await harness.Session.OpenChatAsync(other.Id).WaitAsync(TurnWindDownLimit);

        Assert.False(harness.Session.IsBusy);
        await sending.WaitAsync(TurnWindDownLimit);
    }

    [Fact]
    public async Task Opening_another_chat_completes_even_when_the_turn_teardown_throws()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);
        var (sending, _) = await StartStalledAnswer(harness);
        var isArmed = true;
        harness.Session.StateChanged += () =>
        {
            if (isArmed && !harness.Session.IsBusy)
            {
                isArmed = false;
                throw new InvalidOperationException("Scripted subscriber failure.");
            }
        };

        await harness.Session.OpenChatAsync(other.Id).WaitAsync(TurnWindDownLimit);

        Assert.Equal(other.Id, harness.Session.ChatId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sending.WaitAsync(TurnWindDownLimit));
    }

    [Fact]
    public async Task A_slow_load_does_not_replace_the_chat_the_user_opened_after_it()
    {
        await using var harness = new ChatSessionHarness();
        var first = StoreEmptyChat(harness);
        var second = StoreEmptyChat(harness);
        var firstReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Repository.BeforeGetChat = chatId => chatId == first.Id ? firstReleased.Task : Task.CompletedTask;

        var loadingFirst = harness.Session.OpenChatAsync(first.Id);
        await harness.Session.OpenChatAsync(second.Id).WaitAsync(TurnWindDownLimit);
        firstReleased.SetResult();
        await loadingFirst.WaitAsync(TurnWindDownLimit);

        Assert.Equal(second.Id, harness.Session.ChatId);
    }

    private static async Task<(Task Sending, Guid AskedIn)> StartStalledAnswer(ChatSessionHarness harness)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig svar", reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;

        return (sending, harness.Session.ChatId);
    }

    private static async Task<Guid> LeaveMidAnswer(ChatSessionHarness harness, Guid destination)
    {
        var (sending, askedIn) = await StartStalledAnswer(harness);

        await harness.Session.OpenChatAsync(destination).WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        return askedIn;
    }

    private static ChatEntity StoreEmptyChat(ChatSessionHarness harness) =>
        harness.Repository.Store(new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "stored",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
}
