using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class ChatRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private ChatRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // The "New chat" fallback used to live here. It now belongs to ChatManager,
    // which is the only caller that has a default worth applying, so every
    // repository refuses a blank the same way.
    [Fact]
    public async Task CreateChatAsync_refuses_a_blank_title()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateChatAsync(OwnerId, "   ", assistantId: null));
    }

    [Fact]
    public async Task RenameChatAsync_refuses_a_blank_title_instead_of_doing_nothing()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.RenameChatAsync(OwnerId, chat.Id, "  "));
    }

    [Fact]
    public async Task CreateChatAsync_refuses_a_title_the_column_cannot_hold()
    {
        var tooLong = new string('a', Chat.MaxTitleLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateChatAsync(OwnerId, tooLong, assistantId: null));
    }

    [Fact]
    public async Task AppendMessagesAsync_with_empty_list_is_a_noop()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, []);

        await using var db = await Factory.CreateDbContextAsync();
        var count = await db.ChatMessages.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AppendMessagesAsync_writes_a_single_message_with_an_assigned_id()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hi")]);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.ChatMessages.SingleAsync();
        Assert.Equal(chat.Id, stored.ChatId);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Fact]
    public async Task AppendMessagesAsync_stores_a_batch_in_list_order()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("assistant", "a1"),
            Message("assistant", "a2"),
            Message("assistant", "a3")
        ]);

        await using var db = await Factory.CreateDbContextAsync();
        var contentsBySeq = await db.ChatMessages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.Seq)
            .Select(m => m.Content)
            .ToListAsync();

        Assert.Equal(["a1", "a2", "a3"], contentsBySeq);
    }

    [Fact]
    public async Task Seq_keeps_increasing_across_separate_appends()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "one")]);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("assistant", "two")]);

        await using var db = await Factory.CreateDbContextAsync();
        var sequenceNumbers = await db.ChatMessages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.Seq)
            .Select(m => new { m.Seq, m.Content })
            .ToListAsync();

        Assert.Equal(["one", "two"], sequenceNumbers.Select(x => x.Content));
        Assert.True(sequenceNumbers[0].Seq < sequenceNumbers[1].Seq);
    }

    // Ordering is Seq's job, not CreatedAt's — the timestamps here are written
    // deliberately backwards to prove the clock has no say in it.
    [Fact]
    public async Task GetChatAsync_orders_messages_by_Seq_and_not_by_CreatedAt()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var later = new DateTimeOffset(2026, 3, 1, 12, 0, 5, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "written first", later)]);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("assistant", "written second", earlier)]);

        var reloaded = await Repo.GetChatAsync(OwnerId, chat.Id);

        Assert.Equal(["written first", "written second"], reloaded!.Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task AppendEventAsync_shares_the_sequence_with_messages_so_the_two_interleave()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "first")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, new ChatEvent { Kind = ChatEventKind.Stopped });
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "third")]);

        var reloaded = await Repo.GetChatAsync(OwnerId, chat.Id);

        var stoppedEvent = Assert.Single(reloaded!.Events);
        var messagesBySeq = reloaded.Messages.OrderBy(m => m.Seq).ToList();
        Assert.True(messagesBySeq[0].Seq < stoppedEvent.Seq);
        Assert.True(stoppedEvent.Seq < messagesBySeq[1].Seq);
    }

    // Seq is store-generated, so the value only reaches the caller if EF reads it
    // back. Pinned because nothing in the app depends on it yet, which is exactly
    // when a silent regression to 0 would go unnoticed.
    [Fact]
    public async Task AppendMessagesAsync_reads_the_generated_Seq_back_onto_the_entity()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var message = Message("user", "hi");

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [message]);

        Assert.True(message.Seq > 0);
    }

    // The hole the two Ignore behaviours close. An Update() built from code holds
    // Seq = 0, and EF used to write that over the stored value, moving a message
    // to the front of the transcript. Not a path the app takes today, which is
    // why nothing would have caught it.
    [Fact]
    public async Task An_update_to_a_message_leaves_its_generated_Seq_untouched()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hi")]);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.ChatMessages.SingleAsync();
        var seqBeforeUpdate = stored.Seq;
        var rebuilt = Message("user", "edited");
        rebuilt.Id = stored.Id;
        rebuilt.ChatId = chat.Id;
        db.Entry(stored).State = EntityState.Detached;
        db.Update(rebuilt);
        await db.SaveChangesAsync();

        await using var after = await Factory.CreateDbContextAsync();
        Assert.Equal(seqBeforeUpdate, (await after.ChatMessages.SingleAsync()).Seq);
    }

    // Seq is assigned by the column default, so a write that never touches
    // ChatRepository still lands in order rather than at 0. Note this covers the
    // value only — a bypassing writer also skips the Chats row lock, so it has no
    // ordering guarantee against a concurrent append. Go through the repository.
    [Fact]
    public async Task A_write_that_bypasses_the_repository_still_gets_an_ordered_Seq()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "through the repository")]);

        await using var db = await Factory.CreateDbContextAsync();
        var bypassing = Message("assistant", "straight onto the context", DateTimeOffset.UtcNow);
        bypassing.Id = Guid.NewGuid();
        bypassing.ChatId = chat.Id;
        db.ChatMessages.Add(bypassing);
        await db.SaveChangesAsync();

        var contentsBySeq = await db.ChatMessages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.Seq)
            .Select(m => m.Content)
            .ToListAsync();

        Assert.Equal(["through the repository", "straight onto the context"], contentsBySeq);
    }

    [Fact]
    public async Task AppendEventAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.AppendEventAsync("someone-else", chat.Id, new ChatEvent { Kind = ChatEventKind.Stopped }));
    }

    [Fact]
    public async Task GetChatAsync_returns_null_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        var reloaded = await Repo.GetChatAsync("someone-else", chat.Id);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task AppendMessagesAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.AppendMessagesAsync("someone-else", chat.Id, [Message("user", "hi")]));
    }

    [Fact]
    public async Task AppendMessagesAsync_leaves_UpdatedAt_unchanged_when_the_insert_fails()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hi")]);

        await using var before = await Factory.CreateDbContextAsync();
        var existingId = (await before.ChatMessages.SingleAsync()).Id;
        var updatedAtBeforeFailure = (await before.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt;

        var colliding = Message("assistant", "boom");
        colliding.Id = existingId;

        await Assert.ThrowsAsync<DbUpdateException>(() => Repo.AppendMessagesAsync(OwnerId, chat.Id, [colliding]));

        await using var after = await Factory.CreateDbContextAsync();
        var reloaded = await after.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal(updatedAtBeforeFailure, reloaded.UpdatedAt);
    }

    // A batch is now one INSERT per message, so it can fail partway with earlier
    // rows already written. The transaction is what keeps it all-or-nothing, and
    // that is worth pinning where it is no longer obvious from the call shape.
    [Fact]
    public async Task AppendMessagesAsync_rolls_back_earlier_rows_when_a_later_message_fails()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "already there")]);

        await using var db = await Factory.CreateDbContextAsync();
        var colliding = Message("assistant", "collides on the primary key");
        colliding.Id = (await db.ChatMessages.SingleAsync()).Id;

        await Assert.ThrowsAsync<DbUpdateException>(() => Repo.AppendMessagesAsync(
            OwnerId, chat.Id, [Message("user", "first of the batch"), colliding]));

        var contents = await db.ChatMessages.Select(m => m.Content).ToListAsync();
        Assert.Equal(["already there"], contents);
    }

    [Fact]
    public async Task AppendMessagesAsync_bumps_chat_UpdatedAt_to_last_CreatedAt()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var first = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(2026, 3, 1, 12, 0, 5, TimeSpan.Zero);

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [
            Message("user", "one", first),
            Message("assistant", "two", last)
        ]);

        await using var db = await Factory.CreateDbContextAsync();
        var reloaded = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal(last, reloaded.UpdatedAt);
    }

    private static ChatMessage Message(string role, string content, DateTimeOffset createdAt = default) => new()
    {
        Role = role,
        Content = content,
        CreatedAt = createdAt
    };
}
