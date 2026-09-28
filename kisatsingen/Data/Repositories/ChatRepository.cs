using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace kisatsingen.Data.Repositories;

public sealed class ChatRepository(IDbContextFactory<AppDbContext> factory) : IChatRepository
{
    // Postgres FK-violation SQLSTATE; caught below to translate a benign race
    // into the same InvalidOperationException the pre-check throws. The
    // constraint name is matched too so a future FK added to Chats can't be
    // silently mistranslated as an assistant lookup failure.
    private const string ForeignKeyViolationSqlState = "23503";
    private const string ChatAssistantForeignKeyName = "FK_Chats_Assistants_AssistantId";

    public async Task<Chat> CreateChatAsync(string ownerId, string title, Guid? assistantId, CancellationToken ct = default)
    {
        var normalisedTitle = BoundedText.RequireTrimmed(title, "Chat title", Chat.MaxTitleLength);

        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        string? assistantNameSnapshot = null;

        // The foreign key only proves the assistant exists. Unchecked, this
        // would hand the caller another owner's instructions and files. The
        // same query also fetches the name for the snapshot column so the
        // sidebar can render "Chat with X (deleted)" after the assistant is
        // gone — see Chat.AssistantNameSnapshot for the reasoning.
        if (assistantId is Guid resolvedAssistantId)
        {
            assistantNameSnapshot = await db.Assistants
                .Where(a => a.Id == resolvedAssistantId && a.OwnerId == ownerId)
                .Select(a => a.Name)
                .SingleOrDefaultAsync(ct);

            if (assistantNameSnapshot is null)
            {
                throw new InvalidOperationException(
                    $"Assistant {resolvedAssistantId} is not available to this owner. Re-list assistants and retry, or create the chat without an assistant.");
            }
        }

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            AssistantId = assistantId,
            AssistantNameSnapshot = assistantNameSnapshot,
            Title = normalisedTitle,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Chats.Add(chat);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsAssistantForeignKeyViolation(ex))
        {
            // The pre-check passed but the assistant was deleted between then
            // and the insert (rare — same user in two tabs today, more common
            // once sharing lets a different owner delete). SetNull makes the
            // eventual outcome benign either way; translating the error here
            // gives callers the same InvalidOperationException they would see
            // if the pre-check had lost the race.
            throw new InvalidOperationException(
                $"Assistant {assistantId!.Value} is not available to this owner. Re-list assistants and retry, or create the chat without an assistant.", ex);
        }

        return chat;
    }

    private static bool IsAssistantForeignKeyViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg
            && pg.SqlState == ForeignKeyViolationSqlState
            && pg.ConstraintName == ChatAssistantForeignKeyName;

    public async Task<Chat?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Chats
            .Include(c => c.Turns.OrderBy(t => t.Seq))
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

    public async Task InsertTurnAsync(string ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        turn.ChatId = chatId;
        await TouchChatAsync(db, ownerId, chatId, DateTimeOffset.UtcNow, ct);

        db.ChatTurns.Add(turn);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // Everything a turn's ending can change; identity, prompt and ordering are fixed at insert.
    public async Task UpdateTurnAsync(string ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await TouchChatAsync(db, ownerId, chatId, DateTimeOffset.UtcNow, ct);

        var updatedTurnCount = await db.ChatTurns
            .Where(t => t.Id == turn.Id && t.ChatId == chatId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, turn.Status)
                .SetProperty(t => t.FailedAt, turn.FailedAt)
                .SetProperty(t => t.AnswerJson, turn.AnswerJson)
                .SetProperty(t => t.ServedModelId, turn.ServedModelId)
                .SetProperty(t => t.ResponseId, turn.ResponseId)
                .SetProperty(t => t.FinishReason, turn.FinishReason)
                .SetProperty(t => t.InputTokens, turn.InputTokens)
                .SetProperty(t => t.OutputTokens, turn.OutputTokens)
                .SetProperty(t => t.TotalTokens, turn.TotalTokens)
                .SetProperty(t => t.DurationMs, turn.DurationMs)
                .SetProperty(t => t.TimeToFirstTokenMs, turn.TimeToFirstTokenMs), ct);

        if (updatedTurnCount == 0)
        {
            throw new InvalidOperationException(
                $"Turn {turn.Id} was not found in chat {chatId}. A turn has to be inserted with InsertTurnAsync before its ending can be saved.");
        }

        await transaction.CommitAsync(ct);
    }

    // Does more than its name suggests, and callers depend on all three: it bumps
    // UpdatedAt, it fails the write when the chat is not the caller's, and — the
    // part that is easy to lose — it takes a row lock on the chat that is held
    // until the transaction commits.
    //
    // That lock is what orders inserts across transactions. Seq is drawn at insert
    // time but the row only becomes visible at commit, so two concurrent inserts
    // to one chat could otherwise commit in the opposite order to their Seq and
    // drop a turn into the middle of a transcript a reader has already seen.
    // Serialising them on the chat row is what makes that impossible. Keep this
    // call ahead of the insert.
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
        // Refused rather than a no-op: a rename that quietly does nothing looks
        // identical to one that worked.
        var trimmed = BoundedText.RequireTrimmed(title, "Chat title", Chat.MaxTitleLength);

        await using var db = await factory.CreateDbContextAsync(ct);

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

    public async Task<bool> DeleteChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var deletedCount = await db.Chats
            .Where(c => c.Id == chatId && c.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
