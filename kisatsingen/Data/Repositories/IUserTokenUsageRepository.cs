using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;

namespace kisatsingen.Data.Repositories;

public interface IUserTokenUsageRepository
{
    Task InsertTokenUsageAsync(TokenUsageEntry entry, CancellationToken ct = default);

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

public sealed record TokenUsageEntry(
    Guid OwnerId,
    Guid TurnId,
    Guid? AssistantId,
    string Provider,
    string ModelId,
    MessageUsage Usage,
    TurnStatus Status);
