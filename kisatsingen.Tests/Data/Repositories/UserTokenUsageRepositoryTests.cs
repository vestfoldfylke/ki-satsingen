using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class UserTokenUsageRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private UserTokenUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Stores_the_row_with_the_provided_values()
    {
        var before = DateTimeOffset.UtcNow;

        await Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            "gpt-5-mini",
            inputTokens: 30,
            outputTokens: 12,
            estimatedInputTokens: 5,
            estimatedOutputTokens: 2,
            TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(OwnerId, stored.OwnerId);
        Assert.Equal("openai", stored.Provider);
        Assert.Equal("gpt-5-mini", stored.ModelId);
        Assert.Equal(30, stored.InputTokens);
        Assert.Equal(12, stored.OutputTokens);
        Assert.Equal(5, stored.EstimatedInputTokens);
        Assert.Equal(2, stored.EstimatedOutputTokens);
        Assert.Equal(TurnStatus.Completed, stored.Status);
        Assert.InRange(stored.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Stores_null_counts_when_none_are_provided()
    {
        await Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            "gpt-5-mini",
            inputTokens: null,
            outputTokens: null,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            TurnStatus.Failed);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Null(stored.InputTokens);
        Assert.Null(stored.OutputTokens);
        Assert.Null(stored.EstimatedInputTokens);
        Assert.Null(stored.EstimatedOutputTokens);
    }

    // A provider that reports part of the counts should not have zeros invented
    // for the rest, and one estimated round trip should not overwrite reported ones.
    [Fact]
    public async Task Keeps_reported_and_estimated_counts_independent()
    {
        await Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            "gpt-5-mini",
            inputTokens: 100,
            outputTokens: null,
            estimatedInputTokens: null,
            estimatedOutputTokens: 25,
            TurnStatus.Stopped);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(100, stored.InputTokens);
        Assert.Null(stored.OutputTokens);
        Assert.Null(stored.EstimatedInputTokens);
        Assert.Equal(25, stored.EstimatedOutputTokens);
    }

    [Fact]
    public async Task Accepts_zero_counts_as_distinct_from_null()
    {
        await Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            "gpt-5-mini",
            inputTokens: 0,
            outputTokens: 0,
            estimatedInputTokens: 0,
            estimatedOutputTokens: 0,
            TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(0, stored.InputTokens);
        Assert.Equal(0, stored.OutputTokens);
        Assert.Equal(0, stored.EstimatedInputTokens);
        Assert.Equal(0, stored.EstimatedOutputTokens);
    }

    // Fence-post: right at the max lengths, the row must still store. Values one
    // over are covered by the sibling tests below.
    [Fact]
    public async Task Accepts_string_fields_at_their_max_lengths()
    {
        var maxOwner = new string('o', TokenUsage.MaxOwnerIdLength);
        var maxProvider = new string('p', TokenUsage.MaxProviderLength);
        var maxModel = new string('m', TokenUsage.MaxModelIdLength);

        await Repo.InsertTokenUsageAsync(
            maxOwner,
            maxProvider,
            maxModel,
            inputTokens: 1,
            outputTokens: 1,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();
        Assert.Equal(maxOwner, stored.OwnerId);
        Assert.Equal(maxProvider, stored.Provider);
        Assert.Equal(maxModel, stored.ModelId);
    }

    [Fact]
    public async Task Throws_when_the_provider_is_too_long()
    {
        var tooLong = new string('a', TokenUsage.MaxProviderLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => Repo.InsertTokenUsageAsync(
            OwnerId,
            tooLong,
            "gpt-5-mini",
            inputTokens: 1,
            outputTokens: 1,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            TurnStatus.Completed));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TokenUsages.ToListAsync());
    }

    [Fact]
    public async Task Throws_when_the_owner_id_is_too_long()
    {
        var tooLong = new string('a', TokenUsage.MaxOwnerIdLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => Repo.InsertTokenUsageAsync(
            tooLong,
            "openai",
            "gpt-5-mini",
            inputTokens: 1,
            outputTokens: 1,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            TurnStatus.Completed));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TokenUsages.ToListAsync());
    }

    [Fact]
    public async Task Throws_when_the_model_id_is_too_long()
    {
        var tooLong = new string('a', TokenUsage.MaxModelIdLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            tooLong,
            inputTokens: 1,
            outputTokens: 1,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            TurnStatus.Completed));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TokenUsages.ToListAsync());
    }

    // Persisted by name so a reorder of the enum never renames stored rows.
    [Theory]
    [InlineData(TurnStatus.Running)]
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Stopped)]
    [InlineData(TurnStatus.LeftChat)]
    [InlineData(TurnStatus.Disconnected)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Unfinished)]
    public async Task Persists_status_as_its_enum_name(TurnStatus status)
    {
        await Repo.InsertTokenUsageAsync(
            OwnerId,
            "openai",
            "gpt-5-mini",
            inputTokens: 1,
            outputTokens: 1,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            status);

        await using var db = await Factory.CreateDbContextAsync();
        var storedStatus = await db.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "TokenUsages" """)
            .SingleAsync();

        Assert.Equal(status.ToString(), storedStatus);
    }

    [Fact]
    public async Task Stamps_each_insert_with_a_distinct_id()
    {
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 1, 1, null, null, TurnStatus.Completed);
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 2, 2, null, null, TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var storedIds = await db.TokenUsages.Select(u => u.Id).ToListAsync();

        Assert.Equal(2, storedIds.Count);
        Assert.Equal(2, storedIds.Distinct().Count());
    }

    [Fact]
    public async Task Keeps_every_row_written_for_the_same_owner()
    {
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 1, 1, null, null, TurnStatus.Completed);
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 2, 2, null, null, TurnStatus.Completed);
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 3, 3, null, null, TurnStatus.Failed);

        await using var db = await Factory.CreateDbContextAsync();
        var rows = await db.TokenUsages.Where(u => u.OwnerId == OwnerId).ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal([1L, 2L, 3L], rows.OrderBy(u => u.InputTokens).Select(u => u.InputTokens));
    }

    [Fact]
    public async Task Rows_are_isolated_per_owner()
    {
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 1, 1, null, null, TurnStatus.Completed);
        await Repo.InsertTokenUsageAsync(OtherOwnerId, "openai", "gpt-5-mini", 2, 2, null, null, TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var ownerIds = await db.TokenUsages.Select(u => u.OwnerId).ToListAsync();

        Assert.Equal(2, ownerIds.Count);
        Assert.Contains(OwnerId, ownerIds);
        Assert.Contains(OtherOwnerId, ownerIds);
    }

    // Written near UTC now, never rounded to a whole second: dashboards that
    // bucket by minute rely on the sub-second component being preserved.
    [Fact]
    public async Task Timestamps_the_row_with_utc_now_at_write_time()
    {
        var before = DateTimeOffset.UtcNow;
        await Repo.InsertTokenUsageAsync(OwnerId, "openai", "gpt-5-mini", 1, 1, null, null, TurnStatus.Completed);
        var after = DateTimeOffset.UtcNow;

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(TimeSpan.Zero, stored.Timestamp.Offset);
        Assert.InRange(stored.Timestamp, before, after);
    }

    // Seeds a row straight through the DbContext at a given timestamp — the
    // query tests below need rows backdated past the `since` cutoff, which the
    // repo's InsertTokenUsageAsync (always Now) can't produce.
    private async Task SeedRowAsync(
        string ownerId,
        DateTimeOffset timestamp,
        string provider = "openai",
        string modelId = "gpt-5-mini",
        long? inputTokens = 10,
        long? outputTokens = 20,
        long? estimatedInputTokens = null,
        long? estimatedOutputTokens = null,
        TurnStatus status = TurnStatus.Completed)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Timestamp = timestamp,
            Provider = provider,
            ModelId = modelId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedInputTokens = estimatedInputTokens,
            EstimatedOutputTokens = estimatedOutputTokens,
            Status = status
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sums_totals_across_all_rows_for_the_owner()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, inputTokens: 10, outputTokens: 20, estimatedInputTokens: 3, estimatedOutputTokens: 4);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, inputTokens: 5, outputTokens: 7, estimatedInputTokens: 1, estimatedOutputTokens: 2);
        await SeedRowAsync(OtherOwnerId, DateTimeOffset.UtcNow, inputTokens: 100, outputTokens: 200);

        var totals = await Repo.GetTotalsAsync(OwnerId, since: null);

        Assert.Equal(15, totals.TotalInputTokens);
        Assert.Equal(27, totals.TotalOutputTokens);
        Assert.Equal(4, totals.TotalEstimatedInputTokens);
        Assert.Equal(6, totals.TotalEstimatedOutputTokens);
        Assert.Equal(2, totals.TurnCount);
    }

    [Fact]
    public async Task Totals_exclude_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now.AddDays(-10), inputTokens: 100, outputTokens: 100);
        await SeedRowAsync(OwnerId, now.AddDays(-1), inputTokens: 7, outputTokens: 11);

        var totals = await Repo.GetTotalsAsync(OwnerId, since: now.AddDays(-7));

        Assert.Equal(7, totals.TotalInputTokens);
        Assert.Equal(11, totals.TotalOutputTokens);
        Assert.Equal(1, totals.TurnCount);
    }

    // Daily buckets must respect Europe/Oslo local midnight, not UTC midnight,
    // otherwise an evening Oslo message would land on "tomorrow" on the chart.
    [Fact]
    public async Task Time_series_buckets_by_local_day_boundary()
    {
        // Oslo is UTC+1 in January. These two UTC times both land on 2026-01-15
        // in Oslo (23:00 and 23:30 local), so they must share one Day bucket.
        await SeedRowAsync(OwnerId, new DateTimeOffset(2026, 1, 15, 22, 0, 0, TimeSpan.Zero), inputTokens: 10);
        await SeedRowAsync(OwnerId, new DateTimeOffset(2026, 1, 15, 22, 30, 0, TimeSpan.Zero), inputTokens: 5);

        // Oslo 00:30 on 2026-01-16 — a different local day.
        await SeedRowAsync(OwnerId, new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero), inputTokens: 2);

        var series = await Repo.GetTimeSeriesAsync(OwnerId, since: null, UsageBucket.Day);

        Assert.Equal(2, series.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero), series[0].Bucket);
        Assert.Equal(15, series[0].InputTokens);
        Assert.Equal(2, series[0].TurnCount);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 23, 0, 0, TimeSpan.Zero), series[1].Bucket);
        Assert.Equal(2, series[1].InputTokens);
        Assert.Equal(1, series[1].TurnCount);
    }

    // DST sanity: in July Oslo is UTC+2, so midnight-local sits one hour earlier
    // in UTC than it does in January. The same code path must handle both offsets.
    [Fact]
    public async Task Time_series_bucket_boundary_tracks_summer_time_offset()
    {
        // Oslo 22:00 on 2026-07-15 (both still on the same local day).
        await SeedRowAsync(OwnerId, new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero), inputTokens: 10);

        // Oslo 00:30 on 2026-07-16 — crosses local midnight.
        await SeedRowAsync(OwnerId, new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero), inputTokens: 3);

        var series = await Repo.GetTimeSeriesAsync(OwnerId, since: null, UsageBucket.Day);

        Assert.Equal(2, series.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero), series[0].Bucket);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 22, 0, 0, TimeSpan.Zero), series[1].Bucket);
    }

    [Fact]
    public async Task Time_series_excludes_rows_for_other_owners()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now, inputTokens: 7);
        await SeedRowAsync(OtherOwnerId, now, inputTokens: 1000);

        var series = await Repo.GetTimeSeriesAsync(OwnerId, since: null, UsageBucket.Day);

        Assert.Single(series);
        Assert.Equal(7, series[0].InputTokens);
        Assert.Equal(1, series[0].TurnCount);
    }

    [Fact]
    public async Task Time_series_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now.AddDays(-10), inputTokens: 100);
        await SeedRowAsync(OwnerId, now.AddDays(-1), inputTokens: 7);

        var series = await Repo.GetTimeSeriesAsync(OwnerId, since: now.AddDays(-7), UsageBucket.Day);

        Assert.Single(series);
        Assert.Equal(7, series[0].InputTokens);
    }

    [Fact]
    public async Task Usage_by_provider_excludes_rows_for_other_owners()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now, provider: "openai", inputTokens: 7);
        await SeedRowAsync(OtherOwnerId, now, provider: "anthropic", inputTokens: 1000);

        var rows = await Repo.GetUsageByProviderAsync(OwnerId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal("openai", only.Key);
        Assert.Equal(7, only.InputTokens);
    }

    [Fact]
    public async Task Usage_by_provider_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now.AddDays(-10), provider: "openai", inputTokens: 100);
        await SeedRowAsync(OwnerId, now.AddDays(-1), provider: "openai", inputTokens: 7);

        var rows = await Repo.GetUsageByProviderAsync(OwnerId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(7, only.InputTokens);
        Assert.Equal(1, only.TurnCount);
    }

    [Fact]
    public async Task Usage_by_status_excludes_rows_for_other_owners()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now, status: TurnStatus.Completed);
        await SeedRowAsync(OtherOwnerId, now, status: TurnStatus.Failed);

        var rows = await Repo.GetUsageByStatusAsync(OwnerId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal(TurnStatus.Completed, only.Key);
    }

    [Fact]
    public async Task Usage_by_status_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now.AddDays(-10), status: TurnStatus.Completed);
        await SeedRowAsync(OwnerId, now.AddDays(-1), status: TurnStatus.Failed);

        var rows = await Repo.GetUsageByStatusAsync(OwnerId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(TurnStatus.Failed, only.Key);
    }

    [Fact]
    public async Task Usage_by_model_excludes_rows_for_other_owners()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now, modelId: "gpt-5-mini", inputTokens: 7);
        await SeedRowAsync(OtherOwnerId, now, modelId: "claude-5", inputTokens: 1000);

        var rows = await Repo.GetUsageByModelAsync(OwnerId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal("gpt-5-mini", only.Key);
        Assert.Equal(7, only.InputTokens);
    }

    [Fact]
    public async Task Usage_by_model_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(OwnerId, now.AddDays(-10), modelId: "gpt-5-mini", inputTokens: 100);
        await SeedRowAsync(OwnerId, now.AddDays(-1), modelId: "gpt-5-mini", inputTokens: 7);

        var rows = await Repo.GetUsageByModelAsync(OwnerId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(7, only.InputTokens);
        Assert.Equal(1, only.TurnCount);
    }
}
