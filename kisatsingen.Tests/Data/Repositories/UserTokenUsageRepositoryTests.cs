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
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private UserTokenUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Seeds a row straight through the DbContext at a given timestamp — the
    // query tests below need rows backdated past the `since` cutoff, which the
    // repository's InsertTokenUsageAsync (always now) can't produce.
    private async Task SeedRowAsync(
        Guid userId,
        DateTimeOffset timestamp,
        string provider = "openai",
        string modelId = "gpt-5-mini",
        long? inputTokens = 10,
        long? outputTokens = 20,
        long? estimatedInputTokens = null,
        long? estimatedOutputTokens = null,
        long? cachedInputTokens = null,
        TurnStatus status = TurnStatus.Completed)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Timestamp = timestamp,
            Provider = provider,
            ModelId = modelId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedInputTokens = estimatedInputTokens,
            EstimatedOutputTokens = estimatedOutputTokens,
            CachedInputTokens = cachedInputTokens,
            Status = status
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sums_totals_across_all_rows_for_the_user()
    {
        await SeedRowAsync(UserId, DateTimeOffset.UtcNow, inputTokens: 10, outputTokens: 20, estimatedInputTokens: 3, estimatedOutputTokens: 4, cachedInputTokens: 8);
        await SeedRowAsync(UserId, DateTimeOffset.UtcNow, inputTokens: 5, outputTokens: 7, estimatedInputTokens: 1, estimatedOutputTokens: 2);
        await SeedRowAsync(OtherUserId, DateTimeOffset.UtcNow, inputTokens: 100, outputTokens: 200, cachedInputTokens: 90);

        var totals = await Repo.GetTotalsAsync(UserId, since: null);

        Assert.Equal(15, totals.TotalInputTokens);
        Assert.Equal(27, totals.TotalOutputTokens);
        Assert.Equal(4, totals.TotalEstimatedInputTokens);
        Assert.Equal(6, totals.TotalEstimatedOutputTokens);
        Assert.Equal(8, totals.TotalCachedInputTokens);
        Assert.Equal(2, totals.TurnCount);
    }

    [Fact]
    public async Task Totals_exclude_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now.AddDays(-10), inputTokens: 100, outputTokens: 100);
        await SeedRowAsync(UserId, now.AddDays(-1), inputTokens: 7, outputTokens: 11);

        var totals = await Repo.GetTotalsAsync(UserId, since: now.AddDays(-7));

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
        await SeedRowAsync(UserId, new DateTimeOffset(2026, 1, 15, 22, 0, 0, TimeSpan.Zero), inputTokens: 10);
        await SeedRowAsync(UserId, new DateTimeOffset(2026, 1, 15, 22, 30, 0, TimeSpan.Zero), inputTokens: 5);

        // Oslo 00:30 on 2026-01-16 — a different local day.
        await SeedRowAsync(UserId, new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero), inputTokens: 2);

        var series = await Repo.GetTimeSeriesAsync(UserId, since: null, UsageBucket.Day);

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
        await SeedRowAsync(UserId, new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero), inputTokens: 10);

        // Oslo 00:30 on 2026-07-16 — crosses local midnight.
        await SeedRowAsync(UserId, new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero), inputTokens: 3);

        var series = await Repo.GetTimeSeriesAsync(UserId, since: null, UsageBucket.Day);

        Assert.Equal(2, series.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero), series[0].Bucket);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 22, 0, 0, TimeSpan.Zero), series[1].Bucket);
    }

    [Fact]
    public async Task Time_series_excludes_rows_for_other_users()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now, inputTokens: 7);
        await SeedRowAsync(OtherUserId, now, inputTokens: 1000);

        var series = await Repo.GetTimeSeriesAsync(UserId, since: null, UsageBucket.Day);

        Assert.Single(series);
        Assert.Equal(7, series[0].InputTokens);
        Assert.Equal(1, series[0].TurnCount);
    }

    [Fact]
    public async Task Time_series_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now.AddDays(-10), inputTokens: 100);
        await SeedRowAsync(UserId, now.AddDays(-1), inputTokens: 7);

        var series = await Repo.GetTimeSeriesAsync(UserId, since: now.AddDays(-7), UsageBucket.Day);

        Assert.Single(series);
        Assert.Equal(7, series[0].InputTokens);
    }

    [Fact]
    public async Task Usage_by_provider_excludes_rows_for_other_users()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now, provider: "openai", inputTokens: 7);
        await SeedRowAsync(OtherUserId, now, provider: "anthropic", inputTokens: 1000);

        var rows = await Repo.GetUsageByProviderAsync(UserId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal("openai", only.Key);
        Assert.Equal(7, only.InputTokens);
    }

    [Fact]
    public async Task Usage_by_provider_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now.AddDays(-10), provider: "openai", inputTokens: 100);
        await SeedRowAsync(UserId, now.AddDays(-1), provider: "openai", inputTokens: 7);

        var rows = await Repo.GetUsageByProviderAsync(UserId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(7, only.InputTokens);
        Assert.Equal(1, only.TurnCount);
    }

    [Fact]
    public async Task Usage_by_status_excludes_rows_for_other_users()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now, status: TurnStatus.Completed);
        await SeedRowAsync(OtherUserId, now, status: TurnStatus.Failed);

        var rows = await Repo.GetUsageByStatusAsync(UserId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal(TurnStatus.Completed, only.Key);
    }

    [Fact]
    public async Task Usage_by_status_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now.AddDays(-10), status: TurnStatus.Completed);
        await SeedRowAsync(UserId, now.AddDays(-1), status: TurnStatus.Failed);

        var rows = await Repo.GetUsageByStatusAsync(UserId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(TurnStatus.Failed, only.Key);
    }

    [Fact]
    public async Task Usage_by_model_excludes_rows_for_other_users()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now, modelId: "gpt-5-mini", inputTokens: 7);
        await SeedRowAsync(OtherUserId, now, modelId: "claude-5", inputTokens: 1000);

        var rows = await Repo.GetUsageByModelAsync(UserId, since: null);

        var only = Assert.Single(rows);
        Assert.Equal("gpt-5-mini", only.Key);
        Assert.Equal(7, only.InputTokens);
    }

    [Fact]
    public async Task Usage_by_model_excludes_rows_older_than_the_since_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRowAsync(UserId, now.AddDays(-10), modelId: "gpt-5-mini", inputTokens: 100);
        await SeedRowAsync(UserId, now.AddDays(-1), modelId: "gpt-5-mini", inputTokens: 7);

        var rows = await Repo.GetUsageByModelAsync(UserId, since: now.AddDays(-7));

        var only = Assert.Single(rows);
        Assert.Equal(7, only.InputTokens);
        Assert.Equal(1, only.TurnCount);
    }
}
