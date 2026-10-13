using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IOrganizationTokenUsageRepository
{
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
}
