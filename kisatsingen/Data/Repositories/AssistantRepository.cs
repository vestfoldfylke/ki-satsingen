using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class AssistantRepository(IDbContextFactory<AppDbContext> factory) : IAssistantRepository
{
    public async Task<Assistant> CreateAssistantAsync(string ownerId, string name, string? description, string instructions, CancellationToken ct = default)
    {
        var normalisedName = BoundedText.RequireTrimmed(name, "Assistant name", Assistant.MaxNameLength);
        var normalisedDescription = BoundedText.TrimToNullable(description, "Assistant description", Assistant.MaxDescriptionLength);
        var normalisedInstructions = BoundedText.RequireTrimmed(instructions, "Assistant instructions", Assistant.MaxInstructionsLength);

        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var assistant = new Assistant
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = normalisedName,
            Description = normalisedDescription,
            Instructions = normalisedInstructions,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Assistants.Add(assistant);
        await db.SaveChangesAsync(ct);

        return assistant;
    }

    public async Task<Assistant?> GetAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // File metadata only. Chunks go through IKnowledgeFileRepository — an
        // assistant's files hold every chunk of every document attached to it.
        return await db.Assistants
            .Include(a => a.KnowledgeFiles.OrderBy(f => f.CreatedAt))
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assistantId && a.OwnerId == ownerId, ct);
    }

    public async Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(string ownerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Assistants
            .Where(a => a.OwnerId == ownerId)
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssistantSummary(a.Id, a.Name, a.Description, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task UpdateAssistantAsync(string ownerId, Guid assistantId, string name, string? description, string instructions, CancellationToken ct = default)
    {
        var normalisedName = BoundedText.RequireTrimmed(name, "Assistant name", Assistant.MaxNameLength);
        var normalisedDescription = BoundedText.TrimToNullable(description, "Assistant description", Assistant.MaxDescriptionLength);
        var normalisedInstructions = BoundedText.RequireTrimmed(instructions, "Assistant instructions", Assistant.MaxInstructionsLength);

        await using var db = await factory.CreateDbContextAsync(ct);

        var updatedCount = await db.Assistants
            .Where(a => a.Id == assistantId && a.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Name, normalisedName)
                .SetProperty(a => a.Description, normalisedDescription)
                .SetProperty(a => a.Instructions, normalisedInstructions)
                .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow), ct);

        if (updatedCount == 0)
        {
            throw new InvalidOperationException($"Assistant {assistantId} was not found for the specified owner.");
        }
    }

    public async Task<bool> DeleteAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var deletedCount = await db.Assistants
            .Where(a => a.Id == assistantId && a.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
