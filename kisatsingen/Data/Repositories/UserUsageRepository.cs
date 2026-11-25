using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public sealed class UserUsageRepository(IDbContextFactory<AppDbContext> factory) : IUserUsageRepository
{
    // All bucketed/time-based queries project the row's UTC Timestamp into
    // Europe/Oslo wall-clock before truncating or extracting fields, so day
    // boundaries, weekdays and hour cells line up with what nb-NO users expect
    // through DST. Postgres' date_trunc(text, timestamp) accepts the field as a
    // parameter, so the bucket size is interpolated as a value, not SQL text.
    private const string OsloTz = "Europe/Oslo";

    // Every row's tokens were spent, so token sums cover all rows; turn counts
    // and the status breakdown cover only turns, and the conversion total only
    // conversions.
    private static readonly string ChatTurnKind = nameof(UsageKind.ChatTurn);
    private static readonly string KnowledgeFileConversionKind = nameof(UsageKind.KnowledgeFileConversion);

    public async Task InsertUsageRecordAsync(NewUsageRecord newRecord, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        db.UsageRecords.Add(new UsageRecord
        {
            Id = Guid.NewGuid(),
            Kind = newRecord.Kind,
            UserId = newRecord.UserId,
            ChatId = newRecord.ChatId,
            TurnId = newRecord.TurnId,
            AssistantId = newRecord.AssistantId,
            Timestamp = DateTimeOffset.UtcNow,
            Provider = newRecord.Provider,
            ModelId = newRecord.ModelId,
            InputTokens = newRecord.Usage.InputTokens,
            OutputTokens = newRecord.Usage.OutputTokens,
            EstimatedInputTokens = newRecord.Usage.EstimatedInputTokens,
            EstimatedOutputTokens = newRecord.Usage.EstimatedOutputTokens,
            CachedInputTokens = newRecord.Usage.CachedInputTokens,
            ReasoningTokens = newRecord.Usage.ReasoningTokens,
            Status = newRecord.Status,
            WebSearchCount = newRecord.WebSearchCount
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<UsageSummary> GetTotalsAsync(
        Guid userId,
        DateTimeOffset? since,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.Database.SqlQuery<UsageSummary>(
            $"""
             SELECT
                 COALESCE(SUM("InputTokens"), 0)::bigint AS "TotalInputTokens",
                 COALESCE(SUM("OutputTokens"), 0)::bigint AS "TotalOutputTokens",
                 COALESCE(SUM("EstimatedInputTokens"), 0)::bigint AS "TotalEstimatedInputTokens",
                 COALESCE(SUM("EstimatedOutputTokens"), 0)::bigint AS "TotalEstimatedOutputTokens",
                 COALESCE(SUM("CachedInputTokens"), 0)::bigint AS "TotalCachedInputTokens",
                 COALESCE(SUM("ReasoningTokens"), 0)::bigint AS "TotalReasoningTokens",
                 COALESCE(SUM(COALESCE("InputTokens", 0) + COALESCE("OutputTokens", 0)) FILTER (WHERE "Kind" = {KnowledgeFileConversionKind}), 0)::bigint AS "TotalConversionTokens",
                 COALESCE(SUM("WebSearchCount"), 0)::bigint AS "TotalWebSearches",
                 (COUNT(*) FILTER (WHERE "Kind" = {ChatTurnKind}))::int AS "TurnCount"
             FROM "UsageRecords"
             WHERE ("UserId" = {userId})
               AND ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             """
        ).ToListAsync(ct);

        return rows[0];
    }

    public async Task<IReadOnlyList<UsageTimeBucket>> GetTimeSeriesAsync(
        Guid userId,
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
                 (COUNT(*) FILTER (WHERE "Kind" = {ChatTurnKind}))::int AS "TurnCount"
             FROM "UsageRecords"
             WHERE ("UserId" = {userId})
               AND ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY 1
             ORDER BY 1
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<string>>> GetUsageByProviderAsync(
        Guid userId,
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
                 (COUNT(*) FILTER (WHERE "Kind" = {ChatTurnKind}))::int AS "TurnCount"
             FROM "UsageRecords"
             WHERE ("UserId" = {userId})
               AND ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "Provider"
             ORDER BY "TurnCount" DESC, "InputTokens" DESC, "Key"
             """
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UsageByKey<TurnStatus>>> GetUsageByStatusAsync(
        Guid userId,
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
                 (COUNT(*) FILTER (WHERE "Kind" = {ChatTurnKind}))::int AS "TurnCount"
             FROM "UsageRecords"
             WHERE ("UserId" = {userId})
               AND "Kind" = {ChatTurnKind}
               AND ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "Status"
             ORDER BY "TurnCount" DESC
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

    public async Task<IReadOnlyList<UsageByKey<string>>> GetUsageByModelAsync(
        Guid userId,
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
                 (COUNT(*) FILTER (WHERE "Kind" = {ChatTurnKind}))::int AS "TurnCount"
             FROM "UsageRecords"
             WHERE ("UserId" = {userId})
               AND ({since}::timestamptz IS NULL OR "Timestamp" >= {since})
             GROUP BY "ModelId"
             ORDER BY "TurnCount" DESC, "InputTokens" DESC, "Key"
             """
        ).ToListAsync(ct);
    }
}
