using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class ChatRepository(IDbContextFactory<AppDbContext> factory) : IChatRepository
{
    public async Task<Chat> CreateChatAsync(string ownerId, string title, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = string.IsNullOrWhiteSpace(title) ? "New chat" : title,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Chats.Add(chat);
        await db.SaveChangesAsync(ct);

        return chat;
    }

    public async Task<Chat?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Split query: two collection includes in one statement would return
        // messages x events rows, repeating every message's Content payload once
        // per event.
        return await db.Chats
            .Include(c => c.Messages.OrderBy(m => m.Seq))
            .Include(c => c.Events.OrderBy(e => e.Seq))
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chatId && c.OwnerId == ownerId, ct);
    }

    public async Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Chats
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatSummary(c.Id, c.Title, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task AppendMessagesAsync(string ownerId, Guid chatId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var sequence = await ReserveSequenceAsync(db, messages.Count, ct);

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
            message.ChatId = chatId;
            message.Seq = sequence[i];
            message.CreatedAt = message.CreatedAt == default ? now : message.CreatedAt;
        }

        await TouchChatAsync(db, ownerId, chatId, messages[^1].CreatedAt, ct);

        db.ChatMessages.AddRange(messages);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task AppendEventAsync(string ownerId, Guid chatId, ChatEvent chatEvent, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var sequence = await ReserveSequenceAsync(db, 1, ct);

        chatEvent.Id = chatEvent.Id == Guid.Empty ? Guid.NewGuid() : chatEvent.Id;
        chatEvent.ChatId = chatId;
        chatEvent.Seq = sequence[0];
        chatEvent.CreatedAt = chatEvent.CreatedAt == default ? DateTimeOffset.UtcNow : chatEvent.CreatedAt;

        await TouchChatAsync(db, ownerId, chatId, chatEvent.CreatedAt, ct);

        db.ChatEvents.Add(chatEvent);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // Assigned here rather than by a column default: Postgres does not promise to
    // evaluate defaults in row order for a multi-row insert, and the order within
    // one batch is exactly what must not drift (a tool call has to stay ahead of
    // its result). Reserving up front makes the order ours.
    private static async Task<IReadOnlyList<long>> ReserveSequenceAsync(AppDbContext db, int count, CancellationToken ct) =>
        await db.Database
            .SqlQuery<long>($"""SELECT nextval('chat_entry_seq') AS "Value" FROM generate_series(1, {count})""")
            .ToListAsync(ct);

    private static async Task TouchChatAsync(AppDbContext db, string ownerId, Guid chatId, DateTimeOffset updatedAt, CancellationToken ct)
    {
        var updatedChatCount = await db.Chats
            .Where(c => c.Id == chatId && c.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, updatedAt), ct);

        if (updatedChatCount == 0)
        {
            throw new InvalidOperationException($"Chat {chatId} was not found for the specified owner.");
        }
    }

    public async Task RenameChatAsync(string ownerId, Guid chatId, string title, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var trimmed = title.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var updatedChatCount = await db.Chats
            .Where(c => c.Id == chatId && c.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Title, trimmed)
                .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), ct);

        if (updatedChatCount == 0)
        {
            throw new InvalidOperationException($"Chat {chatId} was not found for the specified owner.");
        }
    }
}
