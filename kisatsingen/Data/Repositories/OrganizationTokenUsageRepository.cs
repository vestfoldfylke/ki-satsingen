using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class OrganizationTokenUsageRepository(IDbContextFactory<AppDbContext> factory) : IOrganizationTokenUsageRepository
{
    // All bucketed/time-based queries project the row's UTC Timestamp into
    // Europe/Oslo wall-clock before truncating or extracting fields, so day
    // boundaries, weekdays and hour cells line up with what nb-NO users expect
    // through DST. Postgres' date_trunc(text, timestamp) accepts the field as a
    // parameter, so the bucket size is interpolated as a value, not SQL text.
    private const string OsloTz = "Europe/Oslo";

    public async Task<TokenUsageSummary> GetTotalsAsync(
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Database.SqlQuery<TokenUsageSummary>(
            $"""
             SELECT
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "TotalInputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "TotalOutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "TotalEstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "TotalEstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             """
        ).ToListAsync(ct);

        return rows[0];
    }

    public async Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        DateTimeOffset? since,
        UsageBucket bucket,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var field = UsageBucketHelpers.BucketField(bucket);

        return await db.Database.SqlQuery<UsageTimeBucket>(
            $"""
             SELECT
                 (date_trunc({field}, "Timestamp" AT TIME ZONE {OsloTz}) AT TIME ZONE {OsloTz}) AS "Bucket",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY 1
             ORDER BY 1
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Database.SqlQuery<UsageByKey<string>>(
            $"""
             SELECT
                 "Provider" AS "Key",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "Provider"
             ORDER BY COUNT(*) DESC
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Database.SqlQuery<UsageByKey<string>>(
            $"""
             SELECT
                 "Status" AS "Key",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "Status"
             ORDER BY COUNT(*) DESC
             """
        ).ToListAsync(ct);

        return [.. rows.Select(r => new UsageByKey<TurnStatus>(
            Enum.Parse<TurnStatus>(r.Key),
            r.InputTokens,
            r.OutputTokens,
            r.EstimatedInputTokens,
            r.EstimatedOutputTokens,
            r.TurnCount))];
    }

    public async Task<IReadOnlyList<UsageByKey<string>>> GetUsagePerUserAsync(
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Database.SqlQuery<UsageByKey<string>>(
            $"""
             SELECT
                 "OwnerId" AS "Key",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "OwnerId"
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<string>>> GetTopUsersByTurnsAsync(
        DateTimeOffset? since,
        int topN,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Database.SqlQuery<UsageByKey<string>>(
            $"""
             SELECT
                 "OwnerId" AS "Key",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "OwnerId"
             ORDER BY COUNT(*) DESC
             LIMIT {topN}
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.Database.SqlQuery<UsageByKey<string>>(
            $"""
             SELECT
                 "ModelId" AS "Key",
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "InputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "OutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "EstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "EstimatedOutputTokens",
                 COUNT(*)::int AS "TurnCount"
             FROM "TokenUsages"
             WHERE ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "ModelId"
             ORDER BY COUNT(*) DESC
             """
        ).ToListAsync(ct);
    }
}
