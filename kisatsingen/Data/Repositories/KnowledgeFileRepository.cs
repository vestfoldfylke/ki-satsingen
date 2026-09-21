using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class KnowledgeFileRepository(IDbContextFactory<AppDbContext> factory) : IKnowledgeFileRepository
{
    // An unbounded range would just be a slower way of loading the whole
    // document into the context window. Public so the retrieval tool can state
    // the limit rather than discover it by being refused.
    public const int MaxChunkSpan = 50;

    public Task<KnowledgeFile> CreateFileForAssistantAsync(string ownerId, Guid assistantId, KnowledgeFileDraft draft, CancellationToken ct = default)
        => CreateAsync(ownerId, assistantId, chatId: null, draft, ct);

    public Task<KnowledgeFile> CreateFileForChatAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft, CancellationToken ct = default)
        => CreateAsync(ownerId, assistantId: null, chatId, draft, ct);

    // The only place the nullable scope pair exists.
    private async Task<KnowledgeFile> CreateAsync(string ownerId, Guid? assistantId, Guid? chatId, KnowledgeFileDraft draft, CancellationToken ct)
    {
        if (draft.Chunks.Count == 0)
        {
            throw new ArgumentException(
                $"Knowledge file '{draft.FileName}' produced no chunks, so there would be nothing to retrieve from it. " +
                "The uploaded bytes are not kept, so an empty file cannot be repaired later — reject the upload instead of storing it.",
                nameof(draft));
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var fileId = Guid.NewGuid();

        var chunks = draft.Chunks
            .Select((chunk, index) => new KnowledgeFileChunk
            {
                Id = Guid.NewGuid(),
                KnowledgeFileId = fileId,
                Sequence = index,
                Heading = chunk.Heading,
                Content = chunk.Content,
                EstimatedTokenCount = chunk.EstimatedTokenCount
            })
            .ToList();

        var file = new KnowledgeFile
        {
            Id = fileId,
            OwnerId = ownerId,
            AssistantId = assistantId,
            ChatId = chatId,
            FileName = draft.FileName,
            ContentType = draft.ContentType,
            SizeBytes = draft.SizeBytes,
            Sha256 = draft.Sha256,
            Summary = draft.Summary,
            TableOfContents = draft.TableOfContents,
            ChunkCount = chunks.Count,
            EstimatedTokenCount = chunks.Sum(c => c.EstimatedTokenCount),
            PageCount = draft.PageCount,
            Language = draft.Language,
            CreatedAt = now,
            Chunks = chunks
        };

        await TouchScopeAsync(db, ownerId, assistantId, chatId, now, ct);

        // One SaveChanges for the whole graph, unlike the per-row loop in
        // ChatRepository.AppendMessagesAsync: that loop exists because Seq is a
        // column default, and Sequence here is assigned above. Do not match it.
        db.KnowledgeFiles.Add(file);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return file;
    }

    // Verifies the parent is the caller's and bumps its UpdatedAt. Unlike
    // ChatRepository.TouchChatAsync this is not also a concurrency guard —
    // files carry no shared ordering sequence to serialise on.
    private static async Task TouchScopeAsync(AppDbContext db, string ownerId, Guid? assistantId, Guid? chatId, DateTimeOffset updatedAt, CancellationToken ct)
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

    public async Task<KnowledgeFile?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.KnowledgeFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == knowledgeFileId && f.OwnerId == ownerId, ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ProjectSummaries(db.KnowledgeFiles.Where(f => f.AssistantId == assistantId && f.OwnerId == ownerId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeFileSummary>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ProjectSummaries(db.KnowledgeFiles.Where(f => f.ChatId == chatId && f.OwnerId == ownerId)).ToListAsync(ct);
    }

    private static IQueryable<KnowledgeFileSummary> ProjectSummaries(IQueryable<KnowledgeFile> files)
        => files
            .OrderBy(f => f.CreatedAt)
            .Select(f => new KnowledgeFileSummary(
                f.Id,
                f.FileName,
                f.ContentType,
                f.SizeBytes,
                f.ChunkCount,
                f.EstimatedTokenCount,
                f.CreatedAt));

    public async Task<IReadOnlyList<KnowledgeFileChunk>> GetChunksAsync(string ownerId, Guid knowledgeFileId, int firstSequence, int lastSequence, CancellationToken ct = default)
    {
        if (lastSequence < firstSequence)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastSequence),
                lastSequence,
                $"Chunk range end must not precede its start (got {firstSequence}..{lastSequence}). Pass the lower sequence first.");
        }

        // long, because in int arithmetic a 0..int.MaxValue range wraps negative
        // and sails past the cap — on a path whose arguments come from a model.
        var requestedSpan = (long)lastSequence - firstSequence + 1;
        if (requestedSpan > MaxChunkSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastSequence),
                lastSequence,
                $"Chunk range {firstSequence}..{lastSequence} spans {requestedSpan} chunks, over the {MaxChunkSpan} allowed in one call. Request a narrower range.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        // Chunks carry no owner, so the filter joins through the file that does.
        return await db.KnowledgeFileChunks
            .Where(c => c.KnowledgeFileId == knowledgeFileId
                && c.KnowledgeFile!.OwnerId == ownerId
                && c.Sequence >= firstSequence
                && c.Sequence <= lastSequence)
            .OrderBy(c => c.Sequence)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var deletedCount = await db.KnowledgeFiles
            .Where(f => f.Id == knowledgeFileId && f.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
