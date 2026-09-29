using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class ConsumptionRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private ConsumptionRepository Repo => new(Factory, NullLogger<ConsumptionRepository>.Instance);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InsertConsumptionAsync_stores_the_row_with_the_provided_values()
    {
        var before = DateTimeOffset.UtcNow;

        var id = await Repo.InsertConsumptionAsync(OwnerId, "openai", "gpt-5-mini", tokenCount: 42, TurnStatus.Running);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Consumptions.SingleAsync();

        Assert.NotNull(id);
        Assert.Equal(id.Value, stored.Id);
        Assert.Equal(OwnerId, stored.OwnerId);
        Assert.Equal("openai", stored.Provider);
        Assert.Equal("gpt-5-mini", stored.ModelId);
        Assert.Equal(42, stored.TokenCount);
        Assert.Equal(TurnStatus.Running, stored.Status);
        Assert.InRange(stored.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task InsertConsumptionAsync_returns_null_when_the_row_cannot_be_stored()
    {
        var tooLong = new string('a', Consumption.MaxProviderLength + 1);

        var id = await Repo.InsertConsumptionAsync(OwnerId, tooLong, "gpt-5-mini", tokenCount: 1, TurnStatus.Running);

        Assert.Null(id);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.Consumptions.ToListAsync());
    }

    [Fact]
    public async Task InsertConsumptionAsync_persists_status_as_its_enum_name()
    {
        await Repo.InsertConsumptionAsync(OwnerId, "openai", "gpt-5-mini", tokenCount: 1, TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var storedStatus = await db.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "Consumptions" """)
            .SingleAsync();

        Assert.Equal(nameof(TurnStatus.Completed), storedStatus);
    }

    [Fact]
    public async Task UpdateConsumptionAsync_overwrites_only_token_count_and_status()
    {
        var id = await Repo.InsertConsumptionAsync(OwnerId, "openai", "gpt-5-mini", tokenCount: 10, TurnStatus.Running);

        await Repo.UpdateConsumptionAsync(id!.Value, OwnerId, tokenCount: 250, TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Consumptions.SingleAsync();

        Assert.Equal(250, stored.TokenCount);
        Assert.Equal(TurnStatus.Completed, stored.Status);
        Assert.Equal("openai", stored.Provider);
        Assert.Equal("gpt-5-mini", stored.ModelId);
        Assert.Equal(OwnerId, stored.OwnerId);
    }

    [Fact]
    public async Task UpdateConsumptionAsync_throws_when_no_row_matches_the_id()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateConsumptionAsync(Guid.NewGuid(), OwnerId, tokenCount: 1, TurnStatus.Completed));
    }

    [Fact]
    public async Task UpdateConsumptionAsync_throws_for_another_owners_row()
    {
        var id = await Repo.InsertConsumptionAsync(OtherOwnerId, "openai", "gpt-5-mini", tokenCount: 10, TurnStatus.Running);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateConsumptionAsync(id!.Value, OwnerId, tokenCount: 999, TurnStatus.Completed));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Consumptions.SingleAsync();
        Assert.Equal(10, stored.TokenCount);
        Assert.Equal(TurnStatus.Running, stored.Status);
    }

    [Fact]
    public async Task Consumption_rows_are_isolated_per_owner()
    {
        await Repo.InsertConsumptionAsync(OwnerId, "openai", "gpt-5-mini", tokenCount: 1, TurnStatus.Completed);
        await Repo.InsertConsumptionAsync(OtherOwnerId, "openai", "gpt-5-mini", tokenCount: 2, TurnStatus.Completed);

        await using var db = await Factory.CreateDbContextAsync();
        var ownerIds = await db.Consumptions.Select(c => c.OwnerId).ToListAsync();

        Assert.Equal(2, ownerIds.Count);
        Assert.Contains(OwnerId, ownerIds);
        Assert.Contains(OtherOwnerId, ownerIds);
    }
}
