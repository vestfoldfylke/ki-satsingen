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

        var send = Assert.Single(harness.Metrics.Named("_Send"));
        Assert.Equal(MetricConstants.MetricsResultLeftChatLabelValue, send.Label(MetricConstants.MetricsResultLabelName));
    }

    [Fact]
    public async Task Leaving_a_chat_mid_answer_is_absent_from_the_failure_counter()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        await LeaveMidAnswer(harness, other.Id);

        Assert.Empty(harness.Metrics.Named("_Failure"));
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

    [Fact]
    public async Task Leaving_a_chat_mid_answer_frees_the_composer()
    {
        await using var harness = new ChatSessionHarness();
        var other = StoreEmptyChat(harness);

        await LeaveMidAnswer(harness, other.Id);

        Assert.False(harness.Session.IsBusy);
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

    private static async Task<Guid> LeaveMidAnswer(ChatSessionHarness harness, Guid destination)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.OnStream = ct => ModelStream.AnsweringThenStalling("halvferdig svar", reached, ct);

        var sending = harness.Session.SendAsync("hei");
        await reached.Task;
        var askedIn = harness.Session.ChatId!.Value;

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
