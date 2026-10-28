using System.Diagnostics;
using System.Linq.Expressions;
using kisatsingen.Data.Entities;
using kisatsingen.Services;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace kisatsingen.Data.Repositories;

public sealed class KnowledgeFileRepository(IDbContextFactory<AppDbContext> factory, int maxEstimatedTokenCount) : IKnowledgeFileRepository
{
    // One definition for both the query projection and the file just saved,
    // so the two can never disagree on what metadata is. Markdown is left out
    // on purpose.
    private static readonly Expression<Func<KnowledgeFile, KnowledgeFileMetadata>> MetadataProjection = f => new KnowledgeFileMetadata(
        f.Id,
        f.FileName,
        f.ContentType,
        f.SizeBytes,
        f.Sha256,
        f.Summary,
        f.LineCount,
        f.EstimatedTokenCount,
        ReadContentOrigin(f.ContentOrigin),
        f.PageCount,
        f.Version,
        f.CreatedAt,
        f.UpdatedAt);

    private static readonly Func<KnowledgeFile, KnowledgeFileMetadata> ToMetadata = MetadataProjection.Compile();

    public Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default)
        => CreateAsync(userId, assistantId, chatId: null, newFile, ct);

    public Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid userId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default)
        => CreateAsync(userId, assistantId: null, chatId, newFile, ct);

    // The single write path, and the only place the nullable scope pair exists.
    // Every stored text passes through here, so this is where the guarantees
    // the read tools depend on are enforced, whatever converter produced it.
    private async Task<KnowledgeFileSaveResult> CreateAsync(Guid userId, Guid? assistantId, Guid? chatId, NewKnowledgeFile newFile, CancellationToken ct)
    {
        var normalisedFileName = BoundedText.RequireTrimmed(newFile.FileName, "Knowledge file name", KnowledgeFile.MaxFileNameLength);
        var normalisedContentType = BoundedText.RequireTrimmed(newFile.ContentType, "Knowledge file content type", KnowledgeFile.MaxContentTypeLength);
        var normalisedSha256 = KnowledgeFileSha256.Normalise(newFile.Sha256);
        var normalisedSummary = BoundedText.TrimToNullable(newFile.Summary, "Knowledge file summary", KnowledgeFile.MaxSummaryLength);

        if (newFile.SizeBytes <= 0)
        {
            throw new ArgumentException(
                $"Knowledge file '{normalisedFileName}' reports {newFile.SizeBytes} bytes; a stored file must have positive size.",
                nameof(newFile));
        }

        if (newFile.PageCount is <= 0)
        {
            throw new ArgumentException(
                $"Knowledge file '{normalisedFileName}' reports {newFile.PageCount} pages; page count must be positive when supplied (null is fine for formats without pages).",
                nameof(newFile));
        }

        if (!Enum.IsDefined(newFile.Origin))
        {
            throw new ArgumentException(
                $"Knowledge file '{normalisedFileName}' has content origin {(int)newFile.Origin}, which is not a defined {nameof(ContentOrigin)}. Add the value to the enum before a converter returns it.",
                nameof(newFile));
        }

        // Before anything is counted, so LineCount matches the stored text.
        var markdown = newFile.Markdown.ReplaceLineEndings("\n");

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new KnowledgeFileSaveResult.Rejected(ConversionRejections.Empty);
        }

        if (markdown.Contains('\0'))
        {
            return new KnowledgeFileSaveResult.Rejected(ConversionRejections.InvalidCharacters);
        }

        var estimatedTokenCount = TokenEstimate.FromCharacters(markdown.Length);
        if (estimatedTokenCount > maxEstimatedTokenCount)
        {
            return new KnowledgeFileSaveResult.Rejected(ConversionRejections.TooManyTokens(maxEstimatedTokenCount));
        }

        var now = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        switch (assistantId, chatId)
        {
            case (Guid resolvedAssistantId, null):
                await LockAssistantForNewFileAsync(db, userId, resolvedAssistantId, now, ct);

                // After the lock: two files added at once are counted one after the other.
                if (await IsAssistantFullAsync(db, resolvedAssistantId, ct))
                {
                    return new KnowledgeFileSaveResult.Rejected(KnowledgeFileSaveRejections.AssistantFull);
                }
                break;
            case (null, Guid resolvedChatId):
                await TouchChatForNewFileAsync(db, userId, resolvedChatId, now, ct);
                break;
            default:
                throw new UnreachableException("A knowledge file is created for an assistant or a chat; the public methods pass exactly one.");
        }

        var file = new KnowledgeFile
        {
            Id = Guid.NewGuid(),
            AssistantId = assistantId,
            ChatId = chatId,
            FileName = normalisedFileName,
            ContentType = normalisedContentType,
            SizeBytes = newFile.SizeBytes,
            Sha256 = normalisedSha256,
            Summary = normalisedSummary,
            Markdown = markdown,
            LineCount = DocumentLines.Count(markdown),
            EstimatedTokenCount = (int)estimatedTokenCount,
            ContentOrigin = newFile.Origin.ToString(),
            PageCount = newFile.PageCount,
            Version = KnowledgeFile.FirstVersion,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.KnowledgeFiles.Add(file);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DuplicateRejection(ex) is { } reason)
        {
            // The transaction rolls back on dispose, taking the scope's
            // UpdatedAt bump with it.
            return new KnowledgeFileSaveResult.Rejected(reason);
        }

        await transaction.CommitAsync(ct);

        return new KnowledgeFileSaveResult.Saved(ToMetadata(file));
    }

    // IsDefined as well, because TryParse also accepts any number ("7").
    private static ContentOrigin? ReadContentOrigin(string stored)
        => Enum.TryParse<ContentOrigin>(stored, out var origin) && Enum.IsDefined(origin) ? origin : null;

    // timestamptz keeps whole microseconds, so without this the metadata
    // returned from a save would differ from the same file read back.
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
        => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    // Only the per-scope hash indexes mean "already attached"; any other
    // unique violation is a bug and stays an exception.
    private static string? DuplicateRejection(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return null;
        }

        return pg.ConstraintName switch
        {
            AppDbContext.KnowledgeFileChatSha256IndexName => KnowledgeFileSaveRejections.DuplicateInChat,
            AppDbContext.KnowledgeFileAssistantSha256IndexName => KnowledgeFileSaveRejections.DuplicateInAssistant,
            _ => null
        };
    }

    // Checks only that the user may use the assistant: whether they may add
    // files to it (CanEditAssistant) is authorized before the repository is
    // called. Locks the assistant's row until the transaction ends by bumping
    // UpdatedAt.
    private static async Task LockAssistantForNewFileAsync(AppDbContext db, Guid userId, Guid assistantId, DateTimeOffset updatedAt, CancellationToken ct)
    {
        var updatedCount = await db.Assistants
            .Where(AssistantAccess.UsableBy(userId))
            .Where(a => a.Id == assistantId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, updatedAt), ct);

        if (updatedCount == 0)
        {
            throw new InvalidOperationException(
                $"Assistant {assistantId} is not available to this user, so no file can be added to it. Re-list the user's assistants and retry.");
        }
    }

    private static async Task<bool> IsAssistantFullAsync(AppDbContext db, Guid assistantId, CancellationToken ct)
    {
        var fileCount = await db.KnowledgeFiles.CountAsync(f => f.AssistantId == assistantId, ct);
        return fileCount >= Assistant.MaxKnowledgeFiles;
    }

    // Verifies the chat is the user's and bumps its UpdatedAt.
    private static async Task TouchChatForNewFileAsync(AppDbContext db, Guid userId, Guid chatId, DateTimeOffset updatedAt, CancellationToken ct)
    {
        var updatedCount = await db.Chats
            .Where(c => c.Id == chatId && c.OwnerId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, updatedAt), ct);

        if (updatedCount == 0)
        {
            throw new InvalidOperationException($"Chat {chatId} was not found for the specified owner.");
        }
    }

    public async Task<KnowledgeFileText?> GetChatFileTextAsync(Guid userId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ownedChats = db.Chats.Where(c => c.OwnerId == userId);
        var file = await db.KnowledgeFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == knowledgeFileId && f.ChatId == chatId && ownedChats.Any(c => c.Id == chatId), ct);

        return file is null ? null : new KnowledgeFileText(ToMetadata(file), file.Markdown);
    }

    // Normalised the way saving does, so the two can never disagree.
    public async Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid userId, Guid chatId, string sha256, CancellationToken ct = default)
    {
        var normalisedSha256 = KnowledgeFileSha256.Normalise(sha256);
        await using var db = await factory.CreateDbContextAsync(ct);
        var ownedChats = db.Chats.Where(c => c.OwnerId == userId);
        return await db.KnowledgeFiles
            .Where(f => f.ChatId == chatId && f.Sha256 == normalisedSha256 && ownedChats.Any(c => c.Id == chatId))
            .Select(MetadataProjection)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var usableAssistants = db.Assistants.Where(AssistantAccess.UsableBy(userId));
        return await ListMetadata(db.KnowledgeFiles.Where(f => f.AssistantId == assistantId && usableAssistants.Any(a => a.Id == assistantId))).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid userId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ownedChats = db.Chats.Where(c => c.OwnerId == userId);
        return await ListMetadata(db.KnowledgeFiles.Where(f => f.ChatId == chatId && ownedChats.Any(c => c.Id == chatId))).ToListAsync(ct);
    }

    private static IQueryable<KnowledgeFileMetadata> ListMetadata(IQueryable<KnowledgeFile> files)
        => files
            .OrderBy(f => f.CreatedAt)
            .Select(MetadataProjection);

    public async Task<bool> DeleteAssistantFileAsync(Guid userId, Guid assistantId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var usableAssistants = db.Assistants.Where(AssistantAccess.UsableBy(userId));
        var deletedCount = await db.KnowledgeFiles
            .Where(f => f.Id == knowledgeFileId && f.AssistantId == assistantId && usableAssistants.Any(a => a.Id == assistantId))
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
