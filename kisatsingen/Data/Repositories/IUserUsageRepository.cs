using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IUserUsageRepository
{
    Task InsertUsageRecordAsync(NewUsageRecord newRecord, CancellationToken ct = default);

    Task<UsageSummary> GetTotalsAsync(
        Guid userId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        Guid userId,
        DateTimeOffset? since,
        UsageBucket bucket,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        Guid userId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        Guid userId,
        DateTimeOffset? since,
        CancellationToken ct = default);

    Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        Guid userId,
        DateTimeOffset? since,
        CancellationToken ct = default);
}
