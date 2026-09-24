using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// Rows go in through the repository so the Seq values are the database's own —
// the property the merge depends on.
[Collection(PostgresCollection.Name)]
public sealed class TranscriptRestoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string PromptInForce = "the prompt in force";

    private ChatRepository Repo => new(fixture.Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Messages_and_events_interleave_in_the_order_they_were_written()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "first")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, Event(ChatEventKind.Stopped));
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "second")]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Collection(entries,
            e => Assert.Equal("first", Assert.IsType<MessageEntry>(e).Message.Text),
            e => Assert.Equal(ChatEventKind.Stopped, Assert.IsType<EventEntry>(e).Kind),
            e => Assert.Equal("second", Assert.IsType<MessageEntry>(e).Message.Text));
    }

    // The branch a transcript ending in a stop takes.
    [Fact]
    public async Task An_event_after_the_last_message_still_reaches_the_transcript()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "only message")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, Event(ChatEventKind.Failed));

        var entries = await RestoreAsync(chat.Id);

        Assert.Collection(entries,
            e => Assert.IsType<MessageEntry>(e),
            e => Assert.Equal(ChatEventKind.Failed, Assert.IsType<EventEntry>(e).Kind));
    }

    [Fact]
    public async Task A_whole_batch_keeps_its_list_order_through_the_merge()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
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

    [Fact]
    public async Task An_assistant_message_reports_the_prompt_recorded_on_the_user_turn()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            new StoredMessage { Role = "user", Content = "hei", SystemPromptSnapshot = "the prompt that produced it" },
            new StoredMessage { Role = "assistant", Content = "hallo" }
        ]);

        var entries = await RestoreAsync(chat.Id);

        var assistant = Assert.IsType<MessageEntry>(entries[1]);
        Assert.Equal("the prompt that produced it", assistant.Metadata!.SystemPrompt);
    }

    [Fact]
    public async Task An_assistant_message_falls_back_to_the_prompt_in_force_when_no_snapshot_was_recorded()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("user", "hei"),
            Message("assistant", "hallo")
        ]);

        var entries = await RestoreAsync(chat.Id);

        var assistant = Assert.IsType<MessageEntry>(entries[1]);
        Assert.Equal(PromptInForce, assistant.Metadata!.SystemPrompt);
    }

    [Fact]
    public async Task A_user_message_carries_no_assistant_metadata()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hei")]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Null(Assert.IsType<MessageEntry>(Assert.Single(entries)).Metadata);
    }

    // A tool call that doesn't survive storage renders as nothing.
    [Fact]
    public async Task A_tool_call_survives_the_round_trip_through_storage()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var withToolCall = new AiMessage(ChatRole.Assistant, [
            new FunctionCallContent("call-1", "get_current_time_utc", new Dictionary<string, object?> { ["timeZone"] = "Europe/Oslo" })
        ]);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [ChatMessageMapper.ToEntity(withToolCall)]);
        var entries = await RestoreAsync(chat.Id);

        var restored = Assert.IsType<MessageEntry>(Assert.Single(entries));
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(restored.Message.Contents));
        Assert.Equal("get_current_time_utc", call.Name);
    }

    // Without it, an unreadable row can't say what wrote it.
    [Fact]
    public async Task A_written_message_records_which_library_version_produced_its_contents()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            ChatMessageMapper.ToEntity(new AiMessage(ChatRole.Assistant, "hei"))
        ]);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var stored = await db.ChatMessages.SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(stored.ContentsSchemaVersion));
    }

    // Otherwise one unreadable row makes the whole chat impossible to open.
    [Fact]
    public async Task A_message_whose_stored_contents_cannot_be_read_degrades_to_its_plain_text()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            new StoredMessage
            {
                Role = "assistant",
                Content = "the readable text",
                ContentsJson = """[{ "$type": "something-a-later-version-renamed" }]"""
            }
        ]);

        var entries = await RestoreAsync(chat.Id);

        var message = Assert.IsType<MessageEntry>(Assert.Single(entries));
        Assert.Equal("the readable text", message.Message.Text);
    }

    [Fact]
    public async Task An_unreadable_message_does_not_take_the_rest_of_the_transcript_with_it()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("user", "before"),
            new StoredMessage { Role = "assistant", Content = "middle", ContentsJson = "{ not json at all" },
            Message("user", "after")
        ]);

        var entries = await RestoreAsync(chat.Id);

        Assert.Equal(
            ["before", "middle", "after"],
            entries.Select(e => Assert.IsType<MessageEntry>(e).Message.Text));
    }

    [Fact]
    public async Task A_chat_with_no_rows_restores_to_an_empty_transcript()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        var entries = await RestoreAsync(chat.Id);

        Assert.Empty(entries);
    }

    private async Task<IReadOnlyList<TranscriptEntry>> RestoreAsync(Guid chatId)
    {
        var chat = await Repo.GetChatAsync(OwnerId, chatId);

        // Pass-through: these tests are about the merge, and it keeps keys visible.
        return TranscriptRestore.Build(chat!.Messages, chat.Events, PromptInForce, key => key.Value, NullLogger.Instance);
    }

    private static StoredMessage Message(string role, string content) => new()
    {
        Role = role,
        Content = content
    };

    private static ChatEvent Event(ChatEventKind kind) => new() { Kind = kind };
}
