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

    [Fact]
    public async Task AppendMessagesAsync_with_empty_list_is_a_noop()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, []);

        await using var db = await Factory.CreateDbContextAsync();
        var count = await db.ChatMessages.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AppendMessagesAsync_writes_a_single_message_with_an_assigned_id()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "hi")]);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.ChatMessages.SingleAsync();
        Assert.Equal(chat.Id, stored.ChatId);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Fact]
    public async Task AppendMessagesAsync_assigns_increasing_Seq_in_list_order_within_a_batch()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

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
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

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
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
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
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "first")]);
        await Repo.AppendEventAsync(OwnerId, chat.Id, new ChatEvent { Kind = ChatEventKind.Stopped });
        await Repo.AppendMessagesAsync(OwnerId, chat.Id, [Message("user", "third")]);

        var reloaded = await Repo.GetChatAsync(OwnerId, chat.Id);

        var stoppedEvent = Assert.Single(reloaded!.Events);
        var messagesBySeq = reloaded.Messages.OrderBy(m => m.Seq).ToList();
        Assert.True(messagesBySeq[0].Seq < stoppedEvent.Seq);
        Assert.True(stoppedEvent.Seq < messagesBySeq[1].Seq);
    }

    [Fact]
    public async Task AppendEventAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.AppendEventAsync("someone-else", chat.Id, new ChatEvent { Kind = ChatEventKind.Stopped }));
    }

    [Fact]
    public async Task GetChatAsync_returns_null_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        var reloaded = await Repo.GetChatAsync("someone-else", chat.Id);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task AppendMessagesAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.AppendMessagesAsync("someone-else", chat.Id, [Message("user", "hi")]));
    }

    [Fact]
    public async Task AppendMessagesAsync_leaves_UpdatedAt_unchanged_when_the_insert_fails()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
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

    [Fact]
    public async Task AppendMessagesAsync_bumps_chat_UpdatedAt_to_last_CreatedAt()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello");
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
