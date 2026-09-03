using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class ChatRepository(IDbContextFactory<AppDbContext> factory) : IChatRepository
{
    public async Task<Chat> CreateChatAsync(string? ownerId, string title, CancellationToken ct = default)
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

    public async Task<Chat?> GetChatAsync(Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Chats
            .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == chatId, ct);
    }

    public async Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string? ownerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Chats
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatSummary(c.Id, c.Title, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task AppendMessagesAsync(Guid chatId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var nextSequence = await db.ChatMessages
            .Where(m => m.ChatId == chatId)
            .Select(m => (int?)m.SequenceNumber)
            .MaxAsync(ct) ?? -1;

        var now = DateTimeOffset.UtcNow;
        var lastCreatedAt = now;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
            message.ChatId = chatId;
            message.SequenceNumber = nextSequence + 1 + i;
            message.CreatedAt = message.CreatedAt == default ? now : message.CreatedAt;
            lastCreatedAt = message.CreatedAt;
            db.ChatMessages.Add(message);
        }

        await db.Chats
            .Where(c => c.Id == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, lastCreatedAt), ct);

        await db.SaveChangesAsync(ct);
    }

    public async Task RenameChatAsync(Guid chatId, string title, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var trimmed = title.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        await db.Chats
            .Where(c => c.Id == chatId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Title, trimmed)
                .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }
}
