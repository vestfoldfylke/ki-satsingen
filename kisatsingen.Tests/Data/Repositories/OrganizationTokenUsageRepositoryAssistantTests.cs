using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class OrganizationTokenUsageRepositoryAssistantTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int TopN = 10;

    private static readonly Guid AliceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private OrganizationTokenUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Named against alphabetical order, so the name tie-breaker cannot pass it.
    [Fact]
    public async Task Ranks_shared_assistants_by_number_of_chats()
    {
        var seldom = await SeedAssistantAsync("Arkiv");
        var often = await SeedAssistantAsync("Saksbehandler");
        await SeedUsageAsync(seldom, AliceId, Guid.NewGuid());
        await SeedUsageAsync(often, AliceId, Guid.NewGuid());
        await SeedUsageAsync(often, AliceId, Guid.NewGuid());

        var top = await Repo.GetTopOrganizationAssistantsAsync(since: null, TopN);

        Assert.Equal(["Saksbehandler", "Arkiv"], top.Select(assistant => assistant.Name));
    }

    // Several turns in one chat, or a retry, are still one chat.
    [Fact]
    public async Task Counts_each_chat_and_each_user_once()
    {
        var assistant = await SeedAssistantAsync("Saksbehandler");
        var aliceChat = Guid.NewGuid();
        await SeedUsageAsync(assistant, AliceId, aliceChat);
        await SeedUsageAsync(assistant, AliceId, aliceChat);
        await SeedUsageAsync(assistant, BobId, Guid.NewGuid());

        var usage = Assert.Single(await Repo.GetTopOrganizationAssistantsAsync(since: null, TopN));

        Assert.Equal((2, 2), (usage.ChatCount, usage.UserCount));
    }

    [Fact]
    public async Task Sums_the_tokens_spent_with_each_assistant()
    {
        var assistant = await SeedAssistantAsync("Saksbehandler");
        await SeedUsageAsync(assistant, AliceId, Guid.NewGuid(), inputTokens: 100, outputTokens: 20);
        await SeedUsageAsync(assistant, BobId, Guid.NewGuid(), inputTokens: 50, outputTokens: 5);

        var usage = Assert.Single(await Repo.GetTopOrganizationAssistantsAsync(since: null, TopN));

        Assert.Equal((150L, 25L), (usage.InputTokens, usage.OutputTokens));
    }

    [Fact]
    public async Task Leaves_out_private_assistants()
    {
        var privateAssistant = await SeedAssistantAsync("Privat", AssistantVisibility.Private);
        await SeedUsageAsync(privateAssistant, AliceId, Guid.NewGuid());

        var top = await Repo.GetTopOrganizationAssistantsAsync(since: null, TopN);

        Assert.Empty(top);
    }

    [Fact]
    public async Task Leaves_out_deleted_assistants()
    {
        await SeedUsageAsync(assistantId: Guid.NewGuid(), AliceId, Guid.NewGuid());

        var top = await Repo.GetTopOrganizationAssistantsAsync(since: null, TopN);

        Assert.Empty(top);
    }

    [Fact]
    public async Task Leaves_out_usage_older_than_the_since_cutoff()
    {
        var assistant = await SeedAssistantAsync("Saksbehandler");
        var now = DateTimeOffset.UtcNow;
        await SeedUsageAsync(assistant, AliceId, Guid.NewGuid(), timestamp: now.AddDays(-30));
        await SeedUsageAsync(assistant, BobId, Guid.NewGuid(), timestamp: now);

        var usage = Assert.Single(await Repo.GetTopOrganizationAssistantsAsync(since: now.AddDays(-7), TopN));

        Assert.Equal(1, usage.ChatCount);
    }

    [Fact]
    public async Task Returns_at_most_top_n_assistants()
    {
        for (var index = 0; index < 3; index++)
        {
            var assistant = await SeedAssistantAsync($"Assistent {index}");
            await SeedUsageAsync(assistant, AliceId, Guid.NewGuid());
        }

        var top = await Repo.GetTopOrganizationAssistantsAsync(since: null, topN: 2);

        Assert.Equal(2, top.Count);
    }

    private async Task<Guid> SeedAssistantAsync(string name, AssistantVisibility visibility = AssistantVisibility.Organization)
    {
        var assistant = new Assistant
        {
            Id = Guid.NewGuid(),
            OwnerId = AliceId,
            Name = name,
            Visibility = visibility
        };

        await using var db = await Factory.CreateDbContextAsync();
        db.Assistants.Add(assistant);
        await db.SaveChangesAsync();
        return assistant.Id;
    }

    private async Task SeedUsageAsync(
        Guid assistantId,
        Guid userId,
        Guid chatId,
        long inputTokens = 10,
        long outputTokens = 20,
        DateTimeOffset? timestamp = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChatId = chatId,
            TurnId = Guid.NewGuid(),
            AssistantId = assistantId,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Provider = "openai",
            ModelId = "gpt-5-mini",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Status = TurnStatus.Completed
        });
        await db.SaveChangesAsync();
    }
}
