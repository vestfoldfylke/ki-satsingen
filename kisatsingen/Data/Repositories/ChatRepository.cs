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
        return await db.Chats
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
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

        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
            message.ChatId = chatId;
            // Postgres' timestamptz only keeps microsecond precision, so a 1-tick
            // (100ns) offset per message would round away — space them a full
            // microsecond apart to keep same-batch messages in a stable order.
            message.CreatedAt = message.CreatedAt == default ? now.AddTicks(i * 10) : message.CreatedAt;
        }

        // Assumes messages is already in chronological order — Chat.UpdatedAt is set
        // from the last element, not the max, so an out-of-order caller would understate it.
        var lastCreatedAt = messages[^1].CreatedAt;

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var updatedChatCount = await db.Chats
            .Where(c => c.Id == chatId && c.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, lastCreatedAt), ct);

        if (updatedChatCount == 0)
        {
            throw new InvalidOperationException($"Chat {chatId} was not found for the specified owner.");
        }

        db.ChatMessages.AddRange(messages);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
