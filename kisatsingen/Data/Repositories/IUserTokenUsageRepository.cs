using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IUserTokenUsageRepository
{
    Task InsertTokenUsageAsync(TokenUsageEntry entry, CancellationToken ct = default);

    Task<TokenUsageSummary> GetTotalsAsync(
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
