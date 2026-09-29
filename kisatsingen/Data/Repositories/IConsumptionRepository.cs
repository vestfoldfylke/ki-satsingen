using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IConsumptionRepository
{
    Task<Guid?> InsertConsumptionAsync(string ownerId, string provider, string modelId, long tokenCount, TurnStatus status, CancellationToken ct = default);
    Task UpdateConsumptionAsync(Guid id, string ownerId, long tokenCount, TurnStatus status, CancellationToken ct = default);
}