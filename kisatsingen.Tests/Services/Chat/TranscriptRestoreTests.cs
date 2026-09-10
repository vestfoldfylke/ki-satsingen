using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.Data;
using Xunit;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// The seam between storage and the reader's transcript. Rows go in through the
// repository so the Seq values are the ones the database actually hands out,
// which is the whole property the merge depends on — a fixture picking its own
// numbers would prove nothing about the real ordering.
[Collection(PostgresCollection.Name)]
public sealed class TranscriptRestoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string PromptInForce = "the prompt in force";

    private ChatRepository Repo => new(fixture.Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task messages_and_events_interleave_in_the_order_they_were_written()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "first")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, Event(ChatEventKind.Stopped));
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "second")]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Collection(entries,
            e => Assert.Equal("first", Assert.IsType<MessageEntry>(e).Message.Text),
            e => Assert.Equal(ChatEventKind.Stopped, Assert.IsType<EventEntry>(e).Kind),
            e => Assert.Equal("second", Assert.IsType<MessageEntry>(e).Message.Text));
    }

    // The merge runs out of messages before it runs out of events here, which is
    // the branch a transcript ending in a stop takes.
    [Fact]
    public async Task an_event_after_the_last_message_still_reaches_the_transcript()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "only message")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, Event(ChatEventKind.Failed));

        var entries = await RestoreAsync(chat.Id);

        Assert.Collection(entries,
            e => Assert.IsType<MessageEntry>(e),
            e => Assert.Equal(ChatEventKind.Failed, Assert.IsType<EventEntry>(e).Kind));
    }

    [Fact]
    public async Task a_whole_batch_keeps_its_list_order_through_the_merge()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("user", "one"),
            Message("assistant", "two"),
            Message("tool", "three"),
            Message("assistant", "four")
        ]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Equal(
            ["one", "two", "three", "four"],
            entries.Select(e => Assert.IsType<MessageEntry>(e).Message.Text));
    }

    // The snapshot is carried forward from the user turn rather than read off the
    // assistant row, so the metadata names the prompt that actually produced it.
    [Fact]
    public async Task an_assistant_message_reports_the_prompt_recorded_on_the_user_turn()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            new StoredMessage { Role = "user", Content = "hei", SystemPromptSnapshot = "the prompt that produced it" },
            new StoredMessage { Role = "assistant", Content = "hallo" }
        ]);

        var entries = await RestoreAsync(chat.Id);

        var assistant = Assert.IsType<MessageEntry>(entries[1]);
        Assert.Equal("the prompt that produced it", assistant.Metadata!.SystemPrompt);
    }

    // Nothing recorded a snapshot, so the prompt in force at load time stands in.
    [Fact]
    public async Task an_assistant_message_falls_back_to_the_prompt_in_force_when_no_snapshot_was_recorded()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("user", "hei"),
            Message("assistant", "hallo")
        ]);

        var entries = await RestoreAsync(chat.Id);

        var assistant = Assert.IsType<MessageEntry>(entries[1]);
        Assert.Equal(PromptInForce, assistant.Metadata!.SystemPrompt);
    }

    [Fact]
    public async Task a_user_message_carries_no_assistant_metadata()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hei")]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Null(Assert.IsType<MessageEntry>(Assert.Single(entries)).Metadata);
    }

    [Fact]
    public async Task a_chat_with_no_rows_restores_to_an_empty_transcript()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        var entries = await RestoreAsync(chat.Id);

        Assert.Empty(entries);
    }

    private async Task<IReadOnlyList<TranscriptEntry>> RestoreAsync(Guid chatId)
    {
        var chat = await Repo.GetChatAsync(OwnerId, chatId);

        return TranscriptRestore.Build(chat!.Messages, chat.Events, PromptInForce);
    }

    private static StoredMessage Message(string role, string content) => new()
    {
        Role = role,
        Content = content
    };

    private static ChatEvent Event(ChatEventKind kind) => new() { Kind = kind };
}
