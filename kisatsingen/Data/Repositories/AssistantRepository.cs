using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class AssistantRepository(IDbContextFactory<AppDbContext> factory) : IAssistantRepository
{
    public async Task<Assistant> CreateAssistantAsync(Guid ownerId, string name, string? description, AssistantVisibility visibility = AssistantVisibility.Private, CancellationToken ct = default)
    {
        var normalisedName = BoundedText.RequireTrimmed(name, "Assistant name", Assistant.MaxNameLength);
        var normalisedDescription = BoundedText.TrimToNullable(description, "Assistant description", Assistant.MaxDescriptionLength);

        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var assistant = new Assistant
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = normalisedName,
            Description = normalisedDescription,
            Visibility = visibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Assistants.Add(assistant);
        await db.SaveChangesAsync(ct);

        return assistant;
    }

    public async Task<Assistant?> GetAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Assistants
            .AsNoTracking()
            .Where(AssistantAccess.UsableBy(userId))
            .FirstOrDefaultAsync(a => a.Id == assistantId, ct);
    }

    public async Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Assistants
            .Where(AssistantAccess.UsableBy(userId))
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssistantSummary(a.Id, a.OwnerId, a.Name, a.Description, a.Visibility, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> UpdateAssistantDetailsAsync(Guid userId, Guid assistantId, string name, string? description, AssistantVisibility visibility, CancellationToken ct = default)
    {
        var normalisedName = BoundedText.RequireTrimmed(name, "Assistant name", Assistant.MaxNameLength);
        var normalisedDescription = BoundedText.TrimToNullable(description, "Assistant description", Assistant.MaxDescriptionLength);

        await using var db = await factory.CreateDbContextAsync(ct);

        var updatedCount = await db.Assistants
            .Where(AssistantAccess.UsableBy(userId))
            .Where(a => a.Id == assistantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Name, normalisedName)
                .SetProperty(a => a.Description, normalisedDescription)
                .SetProperty(a => a.Visibility, visibility)
                .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return updatedCount > 0;
    }

    public async Task<bool> UpdateAssistantInstructionsAsync(Guid userId, Guid assistantId, string? instructions, CancellationToken ct = default)
    {
        var normalisedInstructions = BoundedText.TrimToNullable(instructions, "Assistant instructions", Assistant.MaxInstructionsLength);

        await using var db = await factory.CreateDbContextAsync(ct);

        var updatedCount = await db.Assistants
            .Where(AssistantAccess.UsableBy(userId))
            .Where(a => a.Id == assistantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Instructions, normalisedInstructions)
                .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return updatedCount > 0;
    }

    public async Task<bool> DeleteAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var deletedCount = await db.Assistants
            .Where(AssistantAccess.UsableBy(userId))
            .Where(a => a.Id == assistantId)
            .ExecuteDeleteAsync(ct);
        return deletedCount > 0;
    }
}
