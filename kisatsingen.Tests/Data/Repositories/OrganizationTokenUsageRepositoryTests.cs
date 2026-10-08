using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class OrganizationTokenUsageRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private OrganizationTokenUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Seeds a row straight through the DbContext: this repo has no insert path,
    // and the aggregation queries need rows at specific timestamps/owners/models.
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
    public async Task Totals_treat_null_token_counts_as_zero_and_still_count_the_turn()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, inputTokens: null, outputTokens: null, status: TurnStatus.Failed);

        var totals = await Repo.GetTotalsAsync(since: null);

        Assert.Equal(0, totals.TotalInputTokens);
        Assert.Equal(0, totals.TotalOutputTokens);
        Assert.Equal(1, totals.TurnCount);
    }

    [Fact]
    public async Task Groups_usage_by_provider_with_summed_tokens_and_turn_count()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, provider: "openai", inputTokens: 10, outputTokens: 20);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, provider: "openai", inputTokens: 5, outputTokens: 7);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, provider: "anthropic", inputTokens: 3, outputTokens: 4);

        var rows = (await Repo.GetUsageByProviderAsync(since: null))
            .ToDictionary(r => r.Key);

        Assert.Equal(2, rows.Count);
        Assert.Equal(15, rows["openai"].InputTokens);
        Assert.Equal(27, rows["openai"].OutputTokens);
        Assert.Equal(2, rows["openai"].TurnCount);
        Assert.Equal(3, rows["anthropic"].InputTokens);
        Assert.Equal(1, rows["anthropic"].TurnCount);
    }

    [Fact]
    public async Task Groups_usage_by_status_and_parses_back_to_enum()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, status: TurnStatus.Completed);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, status: TurnStatus.Completed);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, status: TurnStatus.Failed);

        var rows = (await Repo.GetUsageByStatusAsync(since: null))
            .ToDictionary(r => r.Key);

        Assert.Equal(2, rows[TurnStatus.Completed].TurnCount);
        Assert.Equal(1, rows[TurnStatus.Failed].TurnCount);
    }

    [Fact]
    public async Task Per_user_aggregation_sums_each_owner_only_once()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, inputTokens: 10, outputTokens: 20);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, inputTokens: 5, outputTokens: 7);
        await SeedRowAsync(OtherOwnerId, DateTimeOffset.UtcNow, inputTokens: 100, outputTokens: 200);

        var perUser = (await Repo.GetUsagePerUserAsync(since: null))
            .ToDictionary(r => r.Key);

        Assert.Equal(2, perUser.Count);
        Assert.Equal(15, perUser[OwnerId].InputTokens);
        Assert.Equal(27, perUser[OwnerId].OutputTokens);
        Assert.Equal(2, perUser[OwnerId].TurnCount);
        Assert.Equal(100, perUser[OtherOwnerId].InputTokens);
    }

    [Fact]
    public async Task Top_users_by_turns_ranks_by_count_desc_and_limits_to_top_n()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            await SeedRowAsync("alice", now);
        }
        for (var i = 0; i < 5; i++)
        {
            await SeedRowAsync("bob", now);
        }
        await SeedRowAsync("carol", now);

        var top = await Repo.GetTopUsersByTurnsAsync(since: null, topN: 2);

        Assert.Equal(2, top.Count);
        Assert.Equal("bob", top[0].Key);
        Assert.Equal(5, top[0].TurnCount);
        Assert.Equal("alice", top[1].Key);
        Assert.Equal(3, top[1].TurnCount);
    }

    [Fact]
    public async Task Per_model_aggregation_sums_each_model_only_once()
    {
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, modelId: "gpt-5-mini", inputTokens: 10, outputTokens: 20);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, modelId: "gpt-5-mini", inputTokens: 5, outputTokens: 7);
        await SeedRowAsync(OwnerId, DateTimeOffset.UtcNow, modelId: "claude-5", inputTokens: 1, outputTokens: 2);

        var perModel = (await Repo.GetUsageByModelAsync(since: null))
            .ToDictionary(r => r.Key);

        Assert.Equal(15, perModel["gpt-5-mini"].InputTokens);
        Assert.Equal(2, perModel["gpt-5-mini"].TurnCount);
        Assert.Equal(1, perModel["claude-5"].InputTokens);
        Assert.Equal(1, perModel["claude-5"].TurnCount);
    }
}
