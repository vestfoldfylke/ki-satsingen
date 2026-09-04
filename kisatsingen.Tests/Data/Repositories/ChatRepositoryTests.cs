using System.Data.Common;
using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

public sealed class ChatRepositoryTests : IAsyncLifetime
{
    private DbConnection _connection = null!;
    private IDbContextFactory<AppDbContext> _factory = null!;
    private ChatRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new SharedConnectionDbContextFactory(_connection);

        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        _repo = new ChatRepository(_factory);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AppendMessagesAsync_with_empty_list_is_a_noop()
    {
        var chat = await _repo.CreateChatAsync(ownerId: null, "hello");

        await _repo.AppendMessagesAsync(chat.Id, []);

        await using var db = await _factory.CreateDbContextAsync();
        var count = await db.ChatMessages.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AppendMessagesAsync_writes_a_single_message_at_sequence_zero()
    {
        var chat = await _repo.CreateChatAsync(ownerId: null, "hello");

        await _repo.AppendMessagesAsync(chat.Id, [Message("user", "hi")]);

        await using var db = await _factory.CreateDbContextAsync();
        var stored = await db.ChatMessages.SingleAsync();
        Assert.Equal(0, stored.SequenceNumber);
        Assert.Equal(chat.Id, stored.ChatId);
        Assert.NotEqual(Guid.Empty, stored.Id);
    }

    [Fact]
    public async Task AppendMessagesAsync_assigns_monotonic_sequences_after_max()
    {
        var chat = await _repo.CreateChatAsync(ownerId: null, "hello");
        await _repo.AppendMessagesAsync(chat.Id, [Message("system", "sys"), Message("user", "hi")]);

        await _repo.AppendMessagesAsync(chat.Id, [
            Message("assistant", "a1"),
            Message("assistant", "a2"),
            Message("assistant", "a3")
        ]);

        await using var db = await _factory.CreateDbContextAsync();
        var sequences = await db.ChatMessages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.SequenceNumber)
            .Select(m => m.SequenceNumber)
            .ToListAsync();

        Assert.Equal([0, 1, 2, 3, 4], sequences);
    }

    [Fact]
    public async Task AppendMessagesAsync_leaves_UpdatedAt_unchanged_when_the_insert_fails()
    {
        var chat = await _repo.CreateChatAsync(ownerId: null, "hello");
        await _repo.AppendMessagesAsync(chat.Id, [Message("user", "hi")]);

        await using var before = await _factory.CreateDbContextAsync();
        var existingId = (await before.ChatMessages.SingleAsync()).Id;
        var updatedAtBeforeFailure = (await before.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt;

        var colliding = Message("assistant", "boom");
        colliding.Id = existingId;

        await Assert.ThrowsAsync<DbUpdateException>(() => _repo.AppendMessagesAsync(chat.Id, [colliding]));

        await using var after = await _factory.CreateDbContextAsync();
        var reloaded = await after.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal(updatedAtBeforeFailure, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task AppendMessagesAsync_bumps_chat_UpdatedAt_to_last_CreatedAt()
    {
        var chat = await _repo.CreateChatAsync(ownerId: null, "hello");
        var first = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(2026, 3, 1, 12, 0, 5, TimeSpan.Zero);

        await _repo.AppendMessagesAsync(chat.Id, [
            Message("user", "one", first),
            Message("assistant", "two", last)
        ]);

        await using var db = await _factory.CreateDbContextAsync();
        var reloaded = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal(last, reloaded.UpdatedAt);
    }

    private static ChatMessage Message(string role, string content, DateTimeOffset createdAt = default) => new()
    {
        Role = role,
        Content = content,
        CreatedAt = createdAt
    };

    private sealed class SharedConnectionDbContextFactory(DbConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            return new AppDbContext(options);
        }
    }
}
