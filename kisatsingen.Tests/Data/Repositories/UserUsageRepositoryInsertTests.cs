using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class UserUsageRepositoryInsertTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private UserUsageRepository Repo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Stores_the_row_with_the_provided_values()
    {
        var chatId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var turnId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var assistantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var before = DateTimeOffset.UtcNow;

        await InsertAsync(NewRecord(
            chatId: chatId,
            turnId: turnId,
            assistantId: assistantId,
            usage: new MessageUsage(30, 12, 5, 2, CachedInputTokens: 20, ReasoningTokens: 7)));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal(chatId, stored.ChatId);
        Assert.Equal(turnId, stored.TurnId);
        Assert.Equal(assistantId, stored.AssistantId);
        Assert.Equal("openai", stored.Provider);
        Assert.Equal("gpt-5-mini", stored.ModelId);
        Assert.Equal(30, stored.InputTokens);
        Assert.Equal(12, stored.OutputTokens);
        Assert.Equal(5, stored.EstimatedInputTokens);
        Assert.Equal(2, stored.EstimatedOutputTokens);
        Assert.Equal(20, stored.CachedInputTokens);
        Assert.Equal(7, stored.ReasoningTokens);
        Assert.Equal(TurnStatus.Completed, stored.Status);
        Assert.InRange(stored.Timestamp, before, DateTimeOffset.UtcNow);
    }

    // A turn stopped before the provider reported anything has no reported counts.
    [Fact]
    public async Task Stores_null_counts_when_none_are_provided()
    {
        await InsertAsync(NewRecord(usage: new MessageUsage(null, null), status: TurnStatus.Stopped));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();

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
        await InsertAsync(NewRecord(
            usage: new MessageUsage(InputTokens: 100, OutputTokens: null, EstimatedInputTokens: null, EstimatedOutputTokens: 25),
            status: TurnStatus.Stopped));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();

        Assert.Equal(100, stored.InputTokens);
        Assert.Null(stored.OutputTokens);
        Assert.Null(stored.EstimatedInputTokens);
        Assert.Equal(25, stored.EstimatedOutputTokens);
        Assert.Null(stored.CachedInputTokens);
    }

    [Fact]
    public async Task Accepts_zero_counts_as_distinct_from_null()
    {
        await InsertAsync(NewRecord(usage: new MessageUsage(0, 0, 0, 0, CachedInputTokens: 0)));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();

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
        var maxProvider = new string('p', UsageRecord.MaxProviderLength);
        var maxModel = new string('m', UsageRecord.MaxModelIdLength);

        await InsertAsync(NewRecord(provider: maxProvider, modelId: maxModel));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();
        Assert.Equal(maxProvider, stored.Provider);
        Assert.Equal(maxModel, stored.ModelId);
    }

    [Fact]
    public async Task Throws_when_the_provider_is_too_long()
    {
        var tooLong = new string('a', UsageRecord.MaxProviderLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewRecord(provider: tooLong)));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.UsageRecords.ToListAsync());
    }

    [Fact]
    public async Task Throws_when_the_model_id_is_too_long()
    {
        var tooLong = new string('a', UsageRecord.MaxModelIdLength + 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewRecord(modelId: tooLong)));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.UsageRecords.ToListAsync());
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
        await InsertAsync(NewRecord(status: status));

        await using var db = await Factory.CreateDbContextAsync();
        var storedStatus = await db.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "UsageRecords" """)
            .SingleAsync();

        Assert.Equal(status.ToString(), storedStatus);
    }

    [Fact]
    public async Task Stores_an_assistant_file_conversion_with_its_kind_and_assistant_and_no_turn()
    {
        var assistantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var spent = new ConversionTokenUsage("Mistral AI", "mistral-large-latest", new MessageUsage(1_000, 300));

        await InsertAsync(NewUsageRecord.ForAssistantFileConversion(UserId, assistantId, spent));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();
        Assert.Equal(
            (UsageKind.KnowledgeFileConversion, assistantId, (Guid?)null, (Guid?)null, (TurnStatus?)null, "Mistral AI", "mistral-large-latest", 1000L, 300L),
            (stored.Kind, stored.AssistantId, stored.ChatId, stored.TurnId, stored.Status, stored.Provider, stored.ModelId, stored.InputTokens, stored.OutputTokens));
    }

    [Fact]
    public async Task Stores_a_turn_s_web_searches_with_their_kind_and_search_count_and_no_status()
    {
        var turnId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await InsertAsync(NewUsageRecord.ForWebSearch(UserId, Guid.NewGuid(), turnId, assistantId: null, "OpenAI", "gpt-6-luna", new MessageUsage(2_000, 400), webSearchCount: 2));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();
        Assert.Equal(
            ("WebSearch", (Guid?)turnId, (TurnStatus?)null, (int?)2, 2000L, 400L),
            (stored.Kind.ToString(), stored.TurnId, stored.Status, stored.WebSearchCount, stored.InputTokens, stored.OutputTokens));
    }

    // The usage queries read every turn row's status back as a TurnStatus.
    [Fact]
    public async Task The_database_refuses_a_turn_row_without_a_status()
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.UsageRecords.Add(new UsageRecord
        {
            Id = Guid.NewGuid(),
            Kind = UsageKind.ChatTurn,
            UserId = UserId,
            Timestamp = DateTimeOffset.UtcNow,
            Provider = "openai",
            ModelId = "gpt-5-mini"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // The search total sums the count over every row, whatever its kind.
    [Theory]
    [InlineData(UsageKind.WebSearch, null)]
    [InlineData(UsageKind.KnowledgeFileConversion, 2)]
    public async Task The_database_refuses_a_web_search_count_on_any_row_but_a_web_search_row_and_its_absence_on_one(UsageKind kind, int? webSearchCount)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.UsageRecords.Add(new UsageRecord
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            UserId = UserId,
            Timestamp = DateTimeOffset.UtcNow,
            Provider = "openai",
            ModelId = "gpt-6-luna",
            WebSearchCount = webSearchCount
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // Persisted by name, like the status: the usage queries filter on it.
    [Fact]
    public async Task Persists_kind_as_its_enum_name()
    {
        await InsertAsync(NewRecord());

        await using var db = await Factory.CreateDbContextAsync();
        var storedKind = await db.Database
            .SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "UsageRecords" """)
            .SingleAsync();

        Assert.Equal("ChatTurn", storedKind);
    }

    [Fact]
    public async Task Persists_a_file_conversion_s_kind_as_its_enum_name()
    {
        var spent = new ConversionTokenUsage("Mistral AI", "mistral-small-2603", new MessageUsage(1_000, 300));
        await InsertAsync(NewUsageRecord.ForAssistantFileConversion(UserId, Guid.NewGuid(), spent));

        await using var db = await Factory.CreateDbContextAsync();
        var storedKind = await db.Database
            .SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "UsageRecords" """)
            .SingleAsync();

        Assert.Equal("KnowledgeFileConversion", storedKind);
    }

    [Fact]
    public async Task Stamps_each_insert_with_a_distinct_id()
    {
        await InsertAsync(NewRecord());
        await InsertAsync(NewRecord());

        await using var db = await Factory.CreateDbContextAsync();
        var storedIds = await db.UsageRecords.Select(u => u.Id).ToListAsync();

        Assert.Equal(2, storedIds.Count);
        Assert.Equal(2, storedIds.Distinct().Count());
    }

    [Fact]
    public async Task Keeps_every_row_written_for_the_same_user()
    {
        await InsertAsync(NewRecord(usage: new MessageUsage(1, 1)));
        await InsertAsync(NewRecord(usage: new MessageUsage(2, 2)));
        await InsertAsync(NewRecord(usage: new MessageUsage(3, 3), status: TurnStatus.Failed));

        await using var db = await Factory.CreateDbContextAsync();
        var rows = await db.UsageRecords.Where(u => u.UserId == UserId).ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal([1L, 2L, 3L], rows.OrderBy(u => u.InputTokens).Select(u => u.InputTokens));
    }

    [Fact]
    public async Task Rows_are_isolated_per_user()
    {
        await InsertAsync(NewRecord(userId: UserId));
        await InsertAsync(NewRecord(userId: OtherUserId));

        await using var db = await Factory.CreateDbContextAsync();
        var userIds = await db.UsageRecords.Select(u => u.UserId).ToListAsync();

        Assert.Equal(2, userIds.Count);
        Assert.Contains(UserId, userIds);
        Assert.Contains(OtherUserId, userIds);
    }

    // Written near UTC now, never rounded to a whole second: dashboards that
    // bucket by minute rely on the sub-second component being preserved.
    [Fact]
    public async Task Timestamps_the_row_with_utc_now_at_write_time()
    {
        var before = DateTimeOffset.UtcNow;
        await InsertAsync(NewRecord());
        var after = DateTimeOffset.UtcNow;

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.UsageRecords.SingleAsync();

        Assert.Equal(TimeSpan.Zero, stored.Timestamp.Offset);
        Assert.InRange(stored.Timestamp, before, after);
    }

    private Task InsertAsync(NewUsageRecord newRecord) => Repo.InsertUsageRecordAsync(newRecord);

    // Defaults for everything a test is not about, so each names only its subject.
    private static NewUsageRecord NewRecord(
        Guid? userId = null,
        Guid? chatId = null,
        Guid? turnId = null,
        Guid? assistantId = null,
        string provider = "openai",
        string modelId = "gpt-5-mini",
        MessageUsage? usage = null,
        TurnStatus status = TurnStatus.Completed) =>
        NewUsageRecord.ForTurn(
            userId: userId ?? UserId,
            chatId: chatId ?? Guid.NewGuid(),
            turnId: turnId ?? Guid.NewGuid(),
            assistantId: assistantId,
            provider: provider,
            modelId: modelId,
            usage: usage ?? new MessageUsage(1, 1),
            status: status);
}
