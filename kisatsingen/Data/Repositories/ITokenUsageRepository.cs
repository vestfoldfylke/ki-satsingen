using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface ITokenUsageRepository
{
    Task InsertTokenUsageAsync(
        string ownerId,
        string provider,
        string modelId,
        long? inputTokens,
        long? outputTokens,
        long? estimatedInputTokens,
        long? estimatedOutputTokens,
        TurnStatus status,
        CancellationToken ct = default);
    
    Task<IReadOnlyList<TokenUsage>> GetMyTokenUsageAsync(string ownerId, CancellationToken ct = default);
    
    Task<IReadOnlyList<TokenUsage>> GetOrganizationUsageAsync(CancellationToken ct = default);
}
