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

    public Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid ownerId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default)
        => CreateAsync(ownerId, assistantId, chatId: null, newFile, ct);

    public Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid ownerId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default)
        => CreateAsync(ownerId, assistantId: null, chatId, newFile, ct);

    // The single write path, and the only place the nullable scope pair exists.
    // Every stored text passes through here, so this is where the guarantees
    // the read tools depend on are enforced, whatever converter produced it.
    private async Task<KnowledgeFileSaveResult> CreateAsync(Guid ownerId, Guid? assistantId, Guid? chatId, NewKnowledgeFile newFile, CancellationToken ct)
    {
        var normalisedFileName = BoundedText.RequireTrimmed(newFile.FileName, "Knowledge file name", KnowledgeFile.MaxFileNameLength);
        var normalisedContentType = BoundedText.RequireTrimmed(newFile.ContentType, "Knowledge file content type", KnowledgeFile.MaxContentTypeLength);
        var normalisedSha256 = NormaliseSha256(newFile.Sha256);
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
        var file = new KnowledgeFile
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
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

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await TouchScopeAsync(db, ownerId, assistantId, chatId, now, ct);

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

    // Exactly 64 hex chars, normalised to lowercase so the DB representation is
    // canonical regardless of which pipeline produced it — which the unique
    // indexes depend on.
    private static string NormaliseSha256(string sha256)
    {
        if (sha256.Length != KnowledgeFile.Sha256HexLength || !IsLowerableHex(sha256))
        {
            // Truncate the offending input in the message so a caller passing a
            // multi-megabyte blob does not push that whole payload into logs.
            const int previewLength = 16;
            var preview = sha256.Length <= previewLength ? sha256 : sha256[..previewLength] + "…";
            throw new ArgumentException(
                $"SHA-256 must be exactly {KnowledgeFile.Sha256HexLength} hexadecimal characters (got {sha256.Length}: '{preview}').",
                nameof(sha256));
        }
        return sha256.ToLowerInvariant();
    }

    private static bool IsLowerableHex(string value)
    {
        foreach (var c in value)
        {
            var isHex = (c is >= '0' and <= '9') || (c is >= 'a' and <= 'f') || (c is >= 'A' and <= 'F');
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }

    // Verifies the parent is the caller's and bumps its UpdatedAt. Unlike
    // ChatRepository.TouchChatAsync this is not also a concurrency guard —
    // files carry no shared ordering sequence to serialise on.
    private static async Task TouchScopeAsync(AppDbContext db, Guid ownerId, Guid? assistantId, Guid? chatId, DateTimeOffset updatedAt, CancellationToken ct)
    {
        if (assistantId is Guid resolvedAssistantId)
        {
            var updatedCount = await db.Assistants
                .Where(a => a.Id == resolvedAssistantId && a.OwnerId == ownerId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, updatedAt), ct);

            if (updatedCount == 0)
            {
                throw new InvalidOperationException($"Assistant {resolvedAssistantId} was not found for the specified owner.");
            }

            return;
        }

        var resolvedChatId = chatId!.Value;
        var updatedChatCount = await db.Chats
            .Where(c => c.Id == resolvedChatId && c.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, updatedAt), ct);

        if (updatedChatCount == 0)
        {
            throw new InvalidOperationException($"Chat {resolvedChatId} was not found for the specified owner.");
        }
    }

    public async Task<KnowledgeFileMetadata?> GetFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.KnowledgeFiles
            .Where(f => f.Id == knowledgeFileId && f.OwnerId == ownerId)
            .Select(MetadataProjection)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<KnowledgeFileText?> GetChatFileTextAsync(Guid ownerId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var file = await db.KnowledgeFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == knowledgeFileId && f.ChatId == chatId && f.OwnerId == ownerId, ct);

        return file is null ? null : new KnowledgeFileText(ToMetadata(file), file.Markdown);
    }

    // Normalised the way saving does, so the two can never disagree.
    public async Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid ownerId, Guid chatId, string sha256, CancellationToken ct = default)
    {
        var normalisedSha256 = NormaliseSha256(sha256);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.KnowledgeFiles
            .Where(f => f.ChatId == chatId && f.Sha256 == normalisedSha256 && f.OwnerId == ownerId)
            .Select(MetadataProjection)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid ownerId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ListMetadata(db.KnowledgeFiles.Where(f => f.AssistantId == assistantId && f.OwnerId == ownerId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid ownerId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ListMetadata(db.KnowledgeFiles.Where(f => f.ChatId == chatId && f.OwnerId == ownerId)).ToListAsync(ct);
    }

    private static IQueryable<KnowledgeFileMetadata> ListMetadata(IQueryable<KnowledgeFile> files)
        => files
            .OrderBy(f => f.CreatedAt)
            .Select(MetadataProjection);

    public async Task<bool> DeleteFileAsync(Guid ownerId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var deletedCount = await db.KnowledgeFiles
            .Where(f => f.Id == knowledgeFileId && f.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
