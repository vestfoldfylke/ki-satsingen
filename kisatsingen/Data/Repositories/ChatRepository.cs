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

        foreach (var message in messages)
        {
            message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
            message.ChatId = chatId;
            message.CreatedAt = message.CreatedAt == default ? now : message.CreatedAt;
        }

        await TouchChatAsync(db, ownerId, chatId, messages[^1].CreatedAt, ct);

        // One SaveChanges per message, deliberately: Seq comes from a column
        // default, and Postgres does not promise to evaluate defaults in row
        // order for a multi-row insert. Order within a batch is what must not
        // drift — a tool call has to stay ahead of its result — so each row gets
        // its own statement. Do not collapse this into AddRange, and do not
        // reduce it to a MaxBatchSize option: the constraint belongs here, where
        // the reason is visible. A batch is a handful of rows on a path that just
        // spent seconds in the model, so the extra round trips do not register.
        foreach (var message in messages)
        {
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task AppendEventAsync(string ownerId, Guid chatId, ChatEvent chatEvent, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        chatEvent.Id = chatEvent.Id == Guid.Empty ? Guid.NewGuid() : chatEvent.Id;
        chatEvent.ChatId = chatId;
        chatEvent.CreatedAt = chatEvent.CreatedAt == default ? DateTimeOffset.UtcNow : chatEvent.CreatedAt;

        await TouchChatAsync(db, ownerId, chatId, chatEvent.CreatedAt, ct);

        db.ChatEvents.Add(chatEvent);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // Does more than its name suggests, and callers depend on all three: it bumps
    // UpdatedAt, it fails the append when the chat is not the caller's, and — the
    // part that is easy to lose — it takes a row lock on the chat that is held
    // until the transaction commits.
    //
    // That lock is what orders appends across transactions. Seq is drawn at insert
    // time but the row only becomes visible at commit, so two concurrent appends
    // to one chat could otherwise commit in the opposite order to their Seq and
    // drop a message into the middle of a transcript a reader has already seen.
    // Serialising them on the chat row is what makes that impossible. Keep this
    // call ahead of the inserts.
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
