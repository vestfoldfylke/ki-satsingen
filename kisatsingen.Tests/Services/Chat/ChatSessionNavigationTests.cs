using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// The sidebar stays clickable while an answer streams, so a turn must never
// follow the view into another chat.
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

        Assert.All(harness.Repository.MessageChatIds, chatId => Assert.Equal(askedIn, chatId));
        Assert.Contains(harness.Repository.AppendedMessages, message => message.Content == "halvferdig svar");
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_records_that_the_user_left_in_the_chat_it_was_asked_in()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        var askedIn = await LeaveMidAnswer(harness, other.Id);

        Assert.Equal(ChatEventKind.LeftChat, Assert.Single(harness.Repository.AppendedEvents).Kind);
        Assert.Equal(askedIn, Assert.Single(harness.Repository.EventChatIds));
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
        Assert.Empty(harness.Session.Committed);
    }

    // So the URL names the chat before the answer ends, and "New chat" or the
    // sidebar entry navigate away from it rather than to where the user already is.
    [Fact]
    public async Task A_first_message_announces_its_new_chat_while_the_answer_is_still_streaming()
    {
        await using var harness = new ChatSessionHarness();
        Guid? announced = null;
        var wasBusyWhenAnnounced = false;
        harness.Session.ChatCreated += chatId =>
        {
            announced = chatId;
            wasBusyWhenAnnounced = harness.Session.IsBusy;
        };

        var (sending, askedIn) = await StartStalledAnswer(harness);
        harness.Session.Cancel();
        await sending.WaitAsync(TurnWindDownLimit);

        Assert.Equal(askedIn, announced);
        Assert.True(wasBusyWhenAnnounced);
    }

    [Fact]
    public async Task A_chat_created_after_the_user_left_is_neither_announced_nor_shown()
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
        var announcements = 0;
        harness.Session.ChatCreated += _ => announcements++;

        var sending = harness.Session.SendAsync("hei");
        await creating.Task.WaitAsync(TurnWindDownLimit);
        var leaving = harness.Session.LoadAsync(other.Id);
        createReleased.SetResult();
        await leaving.WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        Assert.Equal(0, announcements);
        Assert.Equal(other.Id, harness.Session.ChatId);
    }

    [Fact]
    public async Task A_message_in_an_existing_chat_announces_no_new_chat()
    {
        await using var harness = new ChatSessionHarness();
        var existing = StoreEmptyChat(harness);
        await harness.Session.LoadAsync(existing.Id);
        var announcements = 0;
        harness.Session.ChatCreated += _ => announcements++;

        await harness.Session.SendAsync("hei");

        Assert.Equal(0, announcements);
    }

    [Fact]
    public async Task Starting_a_new_chat_mid_answer_stops_it_as_leaving_the_chat()
    {
        await using var harness = new ChatSessionHarness();
        var (sending, askedIn) = await StartStalledAnswer(harness);

        await harness.Session.LoadAsync(null).WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        Assert.Equal(ChatEventKind.LeftChat, Assert.Single(harness.Repository.AppendedEvents).Kind);
        Assert.Equal(askedIn, Assert.Single(harness.Repository.EventChatIds));
        Assert.Null(harness.Session.ChatId);
    }

    // Checked before awaiting the send: once it has returned, IsBusy is false anyway.
    [Fact]
    public async Task Opening_another_chat_returns_only_once_the_answer_has_wound_down()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);
        var (sending, _) = await StartStalledAnswer(harness);

        await harness.Session.LoadAsync(other.Id).WaitAsync(TurnWindDownLimit);

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

        await harness.Session.LoadAsync(other.Id).WaitAsync(TurnWindDownLimit);

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

        var loadingFirst = harness.Session.LoadAsync(first.Id);
        await harness.Session.LoadAsync(second.Id).WaitAsync(TurnWindDownLimit);
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

        return (sending, harness.Session.ChatId!.Value);
    }

    private static async Task<Guid> LeaveMidAnswer(ChatSessionHarness harness, Guid destination)
    {
        var (sending, askedIn) = await StartStalledAnswer(harness);

        await harness.Session.LoadAsync(destination).WaitAsync(TurnWindDownLimit);
        await sending.WaitAsync(TurnWindDownLimit);

        return askedIn;
    }

    private static ChatEntity StoreEmptyChat(ChatSessionHarness harness)
    {
        var chat = new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "stored",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        harness.Repository.StoredChats[chat.Id] = chat;
        return chat;
    }
}
