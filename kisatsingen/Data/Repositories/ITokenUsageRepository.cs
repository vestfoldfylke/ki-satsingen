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
}
