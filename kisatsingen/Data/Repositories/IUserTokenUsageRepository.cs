using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IUserTokenUsageRepository
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

    Task<TokenUsageSummary> GetTotalsAsync(
        string ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        string ownerId,
        DateTimeOffset? since,
        UsageBucket bucket,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        string ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        string ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        string ownerId,
        DateTimeOffset? since,
        CancellationToken ct = default);
}
