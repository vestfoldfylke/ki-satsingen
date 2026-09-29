using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class ConsumptionRepository(IDbContextFactory<AppDbContext> factory, ILogger<ConsumptionRepository> logger) : IConsumptionRepository
{
    public async Task<Guid?> InsertConsumptionAsync(string ownerId, string provider, string modelId, long tokenCount, TurnStatus status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var consumption = new Consumption
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Timestamp = DateTimeOffset.UtcNow,
            Provider = provider,
            ModelId = modelId,
            TokenCount = tokenCount,
            Status = status
        };

        db.Consumptions.Add(consumption);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "An error occurred while adding consumption: {@Consumption}", consumption);
            return null;
        }

        return consumption.Id;
    }

    public async Task UpdateConsumptionAsync(Guid id, string ownerId, long tokenCount, TurnStatus status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var updatedConsumptionCount = await db.Consumptions
            .Where(c => c.Id == id && c.OwnerId == ownerId)
            .ExecuteUpdateAsync(source => source
                .SetProperty(c => c.TokenCount, tokenCount)
                .SetProperty(c => c.Status, status), ct);

        if (updatedConsumptionCount == 0)
        {
            logger.LogError("Consumption with Id {Guid} and OwnerId {OwnerId} was not found", id, ownerId);
            throw new InvalidOperationException($"Consumption with Id {id} and OwnerId {ownerId} was not found. A consumption entry has to be inserted with InsertConsumptionAsync before it can be updated.");
        }
    }
}