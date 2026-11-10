using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class UserTokenUsageRepositoryInsertTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private UserTokenUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Stores_the_entry_with_the_provided_values()
    {
        var turnId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var assistantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var before = DateTimeOffset.UtcNow;

        await InsertAsync(Entry(
            turnId: turnId,
            assistantId: assistantId,
            usage: new MessageUsage(30, 12, 5, 2, CachedInputTokens: 20)));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(OwnerId, stored.OwnerId);
        Assert.Equal(turnId, stored.TurnId);
        Assert.Equal(assistantId, stored.AssistantId);
        Assert.Equal("openai", stored.Provider);
        Assert.Equal("gpt-5-mini", stored.ModelId);
        Assert.Equal(30, stored.InputTokens);
        Assert.Equal(12, stored.OutputTokens);
        Assert.Equal(5, stored.EstimatedInputTokens);
        Assert.Equal(2, stored.EstimatedOutputTokens);
        Assert.Equal(20, stored.CachedInputTokens);
        Assert.Equal(TurnStatus.Completed, stored.Status);
        Assert.InRange(stored.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Stores_no_assistant_for_a_turn_without_one()
    {
        await InsertAsync(Entry(assistantId: null));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Null(stored.AssistantId);
    }

    // A turn stopped before the provider reported anything has no reported counts.
    [Fact]
    public async Task Stores_null_counts_when_none_are_provided()
    {
        await InsertAsync(Entry(usage: new MessageUsage(null, null), status: TurnStatus.Stopped));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Null(stored.InputTokens);
        Assert.Null(stored.OutputTokens);
        Assert.Null(stored.EstimatedInputTokens);
        Assert.Null(stored.EstimatedOutputTokens);
        Assert.Null(stored.CachedInputTokens);
    }

    // A provider that reports part of the counts should not have zeros invented
    // for the rest, and one estimated round trip should not overwrite reported ones.
    [Fact]
    public async Task Keeps_reported_and_estimated_counts_independent()
    {
        await InsertAsync(Entry(
            usage: new MessageUsage(InputTokens: 100, OutputTokens: null, EstimatedInputTokens: null, EstimatedOutputTokens: 25),
            status: TurnStatus.Stopped));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(100, stored.InputTokens);
        Assert.Null(stored.OutputTokens);
        Assert.Null(stored.EstimatedInputTokens);
        Assert.Equal(25, stored.EstimatedOutputTokens);
        Assert.Null(stored.CachedInputTokens);
    }

    [Fact]
    public async Task Accepts_zero_counts_as_distinct_from_null()
    {
        await InsertAsync(Entry(usage: new MessageUsage(0, 0, 0, 0, CachedInputTokens: 0)));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(0, stored.InputTokens);
        Assert.Equal(0, stored.OutputTokens);
        Assert.Equal(0, stored.EstimatedInputTokens);
        Assert.Equal(0, stored.EstimatedOutputTokens);
        Assert.Equal(0, stored.CachedInputTokens);
    }

    // Fence-post: right at the max lengths, the row must still store. Values one
    // over are covered by the sibling tests below.
    [Fact]
    public async Task Accepts_string_fields_at_their_max_lengths()
    {
        var maxProvider = new string('p', TokenUsage.MaxProviderLength);
        var maxModel = new string('m', TokenUsage.MaxModelIdLength);

        await InsertAsync(Entry(provider: maxProvider, modelId: maxModel));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();
        Assert.Equal(maxProvider, stored.Provider);
        Assert.Equal(maxModel, stored.ModelId);
    }

    [Fact]
    public async Task Throws_when_the_provider_is_too_long()
    {
        var tooLong = new string('a', TokenUsage.MaxProviderLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(Entry(provider: tooLong)));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.TokenUsages.ToListAsync());
    }

    [Fact]
    public async Task Throws_when_the_model_id_is_too_long()
    {
        var tooLong = new string('a', TokenUsage.MaxModelIdLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(Entry(modelId: tooLong)));

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
        await InsertAsync(Entry(status: status));

        await using var db = await Factory.CreateDbContextAsync();
        var storedStatus = await db.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "TokenUsages" """)
            .SingleAsync();

        Assert.Equal(status.ToString(), storedStatus);
    }

    [Fact]
    public async Task Stamps_each_entry_with_a_distinct_id()
    {
        await InsertAsync(Entry());
        await InsertAsync(Entry());

        await using var db = await Factory.CreateDbContextAsync();
        var storedIds = await db.TokenUsages.Select(u => u.Id).ToListAsync();

        Assert.Equal(2, storedIds.Count);
        Assert.Equal(2, storedIds.Distinct().Count());
    }

    [Fact]
    public async Task Keeps_every_entry_recorded_for_the_same_owner()
    {
        await InsertAsync(Entry(usage: new MessageUsage(1, 1)));
        await InsertAsync(Entry(usage: new MessageUsage(2, 2)));
        await InsertAsync(Entry(usage: new MessageUsage(3, 3), status: TurnStatus.Failed));

        await using var db = await Factory.CreateDbContextAsync();
        var rows = await db.TokenUsages.Where(u => u.OwnerId == OwnerId).ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal([1L, 2L, 3L], rows.OrderBy(u => u.InputTokens).Select(u => u.InputTokens));
    }

    [Fact]
    public async Task Entries_are_isolated_per_owner()
    {
        await InsertAsync(Entry(ownerId: OwnerId));
        await InsertAsync(Entry(ownerId: OtherOwnerId));

        await using var db = await Factory.CreateDbContextAsync();
        var ownerIds = await db.TokenUsages.Select(u => u.OwnerId).ToListAsync();

        Assert.Equal(2, ownerIds.Count);
        Assert.Contains(OwnerId, ownerIds);
        Assert.Contains(OtherOwnerId, ownerIds);
    }

    // Written near UTC now, never rounded to a whole second: dashboards that
    // bucket by minute rely on the sub-second component being preserved.
    [Fact]
    public async Task Timestamps_the_entry_with_utc_now_at_write_time()
    {
        var before = DateTimeOffset.UtcNow;
        await InsertAsync(Entry());
        var after = DateTimeOffset.UtcNow;

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.TokenUsages.SingleAsync();

        Assert.Equal(TimeSpan.Zero, stored.Timestamp.Offset);
        Assert.InRange(stored.Timestamp, before, after);
    }

    private Task InsertAsync(TokenUsageEntry entry) => Repo.InsertTokenUsageAsync(entry);

    // Defaults for everything a test is not about, so each names only its subject.
    private static TokenUsageEntry Entry(
        Guid? ownerId = null,
        Guid? turnId = null,
        Guid? assistantId = null,
        string provider = "openai",
        string modelId = "gpt-5-mini",
        MessageUsage? usage = null,
        TurnStatus status = TurnStatus.Completed) =>
        new(
            OwnerId: ownerId ?? OwnerId,
            TurnId: turnId ?? Guid.NewGuid(),
            AssistantId: assistantId,
            Provider: provider,
            ModelId: modelId,
            Usage: usage ?? new MessageUsage(1, 1),
            Status: status);
}
