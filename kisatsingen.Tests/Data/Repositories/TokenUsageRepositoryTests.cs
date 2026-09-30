using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class TokenUsageRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private TokenUsageRepository Repo => new(Factory);

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
}
