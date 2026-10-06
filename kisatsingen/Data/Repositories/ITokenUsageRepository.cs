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

    // AdminUsage
    Task<TokenUsageSummary> GetTotalsAsync(
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        DateTimeOffset? since,
        UsageBucket bucket,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsagePerUserAsync(
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetTopUsersByTurnsAsync(
        DateTimeOffset? since,
        int topN,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        DateTimeOffset? since,
        CancellationToken ct = default);

    // MyUsage
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
