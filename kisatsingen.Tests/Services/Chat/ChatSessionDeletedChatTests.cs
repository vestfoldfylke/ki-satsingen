using kisatsingen.Constants;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

// A chat deleted while open, as the retention cleanup can do to a chat left in a tab.
public sealed class ChatSessionDeletedChatTests
{
    [Fact]
    public async Task A_message_sent_in_a_deleted_chat_shows_that_the_chat_is_not_found()
    {
        await using var harness = await OpenChatThenDeleteItAsync();

        await harness.Session.SendAsync("hei");

        Assert.True(harness.Session.IsChatNotFound);
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
