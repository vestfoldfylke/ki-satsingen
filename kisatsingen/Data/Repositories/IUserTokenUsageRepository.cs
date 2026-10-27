using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IUserTokenUsageRepository
{
    Task InsertTokenUsageAsync(
        Guid ownerId,
        string provider,
        string modelId,
        long? inputTokens,
        long? outputTokens,
        long? estimatedInputTokens,
        long? estimatedOutputTokens,
        TurnStatus status,
        CancellationToken ct = default);

    Task<TokenUsageSummary> GetTotalsAsync(
        Guid ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        Guid ownerId,
        DateTimeOffset? since,
        UsageBucket bucket,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        Guid ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        Guid ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        Guid ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);
}
