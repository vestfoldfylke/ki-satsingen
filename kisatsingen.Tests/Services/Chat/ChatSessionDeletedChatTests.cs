using kisatsingen.Constants;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// A chat deleted while open, as the retention cleanup can do to a chat left in a tab.
public sealed class ChatSessionDeletedChatTests
{
    [Fact]
    public async Task A_message_sent_in_a_deleted_chat_says_it_was_not_sent_because_the_chat_is_deleted()
    {
        await using var harness = await OpenChatThenDeleteItAsync();

        await harness.Session.SendAsync("hei");

        Assert.Equal("Meldingen ble ikke sendt fordi samtalen er slettet", harness.VisibleNotice);
    }

    [Fact]
    public async Task A_message_sent_in_a_deleted_chat_never_reaches_the_model()
    {
        await using var harness = await OpenChatThenDeleteItAsync();

        await harness.Session.SendAsync("hei");

        Assert.Empty(harness.Catalog.ResolvedKeys);
    }

    [Fact]
    public async Task A_message_sent_in_a_deleted_chat_is_counted_as_chat_deleted_and_not_as_a_failure()
    {
        await using var harness = await OpenChatThenDeleteItAsync();

        await harness.Session.SendAsync("hei");

        var send = Assert.Single(harness.Metrics.Named(RecordingMetricsService.SendCounter));
        Assert.Equal(MetricConstants.MetricsResultChatDeletedLabelValue, send.Label(MetricConstants.MetricsResultLabelName));
        Assert.Empty(harness.Metrics.Named(RecordingMetricsService.FailureCounter));
    }

    [Fact]
    public async Task A_deleted_chat_takes_no_more_messages()
    {
        await using var harness = await OpenChatThenDeleteItAsync();

        await harness.Session.SendAsync("hei");

        Assert.True(harness.Session.IsChatDeleted);
        Assert.False(harness.Session.CanSend);
    }

    [Fact]
    public async Task A_deleted_chat_is_removed_from_the_chat_list()
    {
        await using var harness = await OpenChatThenDeleteItAsync();
        var deletedChatId = harness.Session.ChatId;
        await harness.Manager.CreateChatAsync(deletedChatId, "stored", assistantId: null, CancellationToken.None);

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain(harness.Manager.Entries, entry => entry.Id == deletedChatId);
    }

    [Fact]
    public async Task Starting_a_new_chat_for_the_unsent_message_hands_back_the_message()
    {
        await using var harness = await OpenChatThenDeleteItAsync();
        await harness.Session.SendAsync("hei");

        var unsentMessage = await harness.Session.StartNewChatForUnsentMessageAsync();

        Assert.Equal("hei", unsentMessage);
    }

    [Fact]
    public async Task Starting_a_new_chat_for_the_unsent_message_opens_an_empty_chat_that_takes_messages()
    {
        await using var harness = await OpenChatThenDeleteItAsync();
        var deletedChatId = harness.Session.ChatId;
        await harness.Session.SendAsync("hei");

        await harness.Session.StartNewChatForUnsentMessageAsync();

        Assert.NotEqual(deletedChatId, harness.Session.ChatId);
        Assert.Empty(harness.Session.Transcript);
        Assert.True(harness.Session.CanSend);
    }

    [Fact]
    public async Task Starting_a_new_chat_for_the_unsent_message_keeps_the_model_it_was_sent_with()
    {
        await using var harness = await OpenChatThenDeleteItAsync();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);
        await harness.Session.SendAsync("hei");

        await harness.Session.StartNewChatForUnsentMessageAsync();

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task There_is_no_unsent_message_in_a_chat_that_was_not_deleted()
    {
        await using var harness = new ChatSessionHarness();

        var unsentMessage = await harness.Session.StartNewChatForUnsentMessageAsync();

        Assert.Null(unsentMessage);
    }

    private static async Task<ChatSessionHarness> OpenChatThenDeleteItAsync()
    {
        var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            Title = "stored",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await harness.Session.OpenChatAsync(chat.Id);
        harness.Repository.DeletedBehindTheSessionsBack.Add(chat.Id);
        return harness;
    }
}
