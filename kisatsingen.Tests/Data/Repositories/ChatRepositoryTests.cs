using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using System.Text.Json;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class ChatRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private ChatRepository Repo => new(Factory);

    private AssistantRepository AssistantRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
    
    [Fact]
    public async Task CreateChatAsync_refuses_a_blank_title()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateChatAsync(OwnerId, "   ", assistantId: null));
    }

    [Fact]
    public async Task RenameChatAsync_refuses_a_blank_title_instead_of_doing_nothing()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.RenameChatAsync(OwnerId, chat.Id, "  "));
    }

    [Fact]
    public async Task CreateChatAsync_refuses_a_title_the_column_cannot_hold()
    {
        var tooLong = new string('a', Chat.MaxTitleLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateChatAsync(OwnerId, tooLong, assistantId: null));
    }

    [Fact]
    public async Task CreateChatAsync_snapshots_the_assistant_name()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Legal Advisor", null, "x");

        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        // Reload from disk rather than trust the returned entity: a future edit
        // that populates the in-memory object but leaves the column out of the
        // INSERT (e.g. AfterSaveBehavior.Ignore) would slip past a check on the
        // returned instance.
        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal("Legal Advisor", stored.AssistantNameSnapshot);
    }

    [Fact]
    public async Task CreateChatAsync_leaves_the_snapshot_null_when_no_assistant_is_attached()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Null(stored.AssistantNameSnapshot);
    }

    [Fact]
    public async Task Deleting_the_assistant_nulls_the_id_but_keeps_the_snapshot()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Legal Advisor", null, "x");
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        await AssistantRepo.DeleteAssistantAsync(OwnerId, assistant.Id);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Null(stored.AssistantId);
        Assert.Equal("Legal Advisor", stored.AssistantNameSnapshot);
    }

    [Fact]
    public async Task Renaming_the_assistant_does_not_rewrite_older_chats_snapshots()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Legal Advisor", null, "x");
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        await AssistantRepo.UpdateAssistantAsync(OwnerId, assistant.Id, "Legal Advisor v2", null, "x");

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Equal("Legal Advisor", stored.AssistantNameSnapshot);
    }


    [Fact]
    public async Task InsertTurnAsync_writes_the_turn_into_its_chat()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("hi"));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.ChatTurns.SingleAsync();
        Assert.Equal((chat.Id, "hi"), (stored.ChatId, stored.Prompt));
    }

    [Fact]
    public async Task Seq_keeps_increasing_across_separate_inserts()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("one"));
        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("two"));

        await using var db = await Factory.CreateDbContextAsync();
        var sequenceNumbers = await db.ChatTurns
            .Where(t => t.ChatId == chat.Id)
            .OrderBy(t => t.Seq)
            .Select(t => new { t.Seq, t.Prompt })
            .ToListAsync();

        Assert.Equal(["one", "two"], sequenceNumbers.Select(x => x.Prompt));
        Assert.True(sequenceNumbers[0].Seq < sequenceNumbers[1].Seq);
    }

    // Ordering is Seq's job, not StartedAt's — the timestamps here are written
    // deliberately backwards to prove the clock has no say in it.
    [Fact]
    public async Task GetChatAsync_orders_turns_by_Seq_and_not_by_StartedAt()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var later = new DateTimeOffset(2026, 3, 1, 12, 0, 5, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("written first", later));
        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("written second", earlier));

        var reloaded = await Repo.GetChatAsync(OwnerId, chat.Id);

        Assert.Equal(["written first", "written second"], reloaded!.Turns.Select(t => t.Prompt));
    }

    // Seq is store-generated, so the value only reaches the caller if EF reads it
    // back. Pinned because nothing in the app depends on it yet, which is exactly
    // when a silent regression to 0 would go unnoticed.
    [Fact]
    public async Task InsertTurnAsync_reads_the_generated_Seq_back_onto_the_entity()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var turn = Row("hi");

        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);

        Assert.True(turn.Seq > 0);
    }

    [Fact]
    public async Task UpdateTurnAsync_stores_how_the_turn_ended()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var turn = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);

        await Repo.UpdateTurnAsync(OwnerId, chat.Id, Row("hi", id: turn.Id, status: "Completed", answerJson: """[{"kind":"text"}]""", durationMs: 1200));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.ChatTurns.SingleAsync();
        Assert.Equal(("Completed", """[{"kind":"text"}]""", (long?)1200), (stored.Status, stored.AnswerJson, stored.DurationMs));
    }

    // The update is the only rewrite a turn gets. Seq is set to be ignored on
    // every write; this is the write that proves it.
    [Fact]
    public async Task UpdateTurnAsync_leaves_the_turns_place_in_the_chat_untouched()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var turn = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);

        await Repo.UpdateTurnAsync(OwnerId, chat.Id, Row("hi", id: turn.Id, status: "Completed"));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(turn.Seq, (await db.ChatTurns.SingleAsync()).Seq);
    }

    [Fact]
    public async Task UpdateTurnAsync_refuses_a_turn_that_was_never_inserted()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateTurnAsync(OwnerId, chat.Id, Row("never inserted", status: "Completed")));
    }

    [Fact]
    public async Task UpdateTurnAsync_refuses_a_turn_from_another_chat()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var other = await Repo.CreateChatAsync(OwnerId, "other", assistantId: null);
        var turn = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateTurnAsync(OwnerId, other.Id, Row("hi", id: turn.Id, status: "Completed")));
    }

    // Seq is assigned by the column default, so a write that never touches
    // ChatRepository still lands in order rather than at 0. Note this covers the
    // value only — a bypassing writer also skips the Chats row lock, so it has no
    // ordering guarantee against a concurrent insert. Go through the repository.
    [Fact]
    public async Task A_write_that_bypasses_the_repository_still_gets_an_ordered_Seq()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.InsertTurnAsync(OwnerId, chat.Id, Row("through the repository"));

        await using var db = await Factory.CreateDbContextAsync();
        var bypassing = Row("straight onto the context");
        bypassing.ChatId = chat.Id;
        db.ChatTurns.Add(bypassing);
        await db.SaveChangesAsync();

        var promptsBySeq = await db.ChatTurns
            .Where(t => t.ChatId == chat.Id)
            .OrderBy(t => t.Seq)
            .Select(t => t.Prompt)
            .ToListAsync();

        Assert.Equal(["through the repository", "straight onto the context"], promptsBySeq);
    }

    [Fact]
    public async Task GetChatAsync_returns_null_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        var reloaded = await Repo.GetChatAsync("someone-else", chat.Id);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task InsertTurnAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.InsertTurnAsync("someone-else", chat.Id, Row("hi")));
    }

    [Fact]
    public async Task UpdateTurnAsync_throws_when_the_chat_belongs_to_a_different_owner()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var turn = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateTurnAsync("someone-else", chat.Id, Row("hi", id: turn.Id, status: "Completed")));
    }

    [Fact]
    public async Task InsertTurnAsync_leaves_UpdatedAt_unchanged_when_the_insert_fails()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var existing = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, existing);

        await using var before = await Factory.CreateDbContextAsync();
        var updatedAtBeforeFailure = (await before.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt;

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Repo.InsertTurnAsync(OwnerId, chat.Id, Row("collides on the primary key", id: existing.Id)));

        await using var after = await Factory.CreateDbContextAsync();
        Assert.Equal(updatedAtBeforeFailure, (await after.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt);
    }

    [Fact]
    public async Task UpdateTurnAsync_moves_the_chat_to_the_top_of_the_list()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var turn = Row("hi");
        await Repo.InsertTurnAsync(OwnerId, chat.Id, turn);
        await using var before = await Factory.CreateDbContextAsync();
        var updatedAtAtInsert = (await before.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt;

        await Repo.UpdateTurnAsync(OwnerId, chat.Id, Row("hi", id: turn.Id, status: "Completed"));

        await using var after = await Factory.CreateDbContextAsync();
        Assert.True((await after.Chats.SingleAsync(c => c.Id == chat.Id)).UpdatedAt > updatedAtAtInsert);
    }

    // The full path an answer takes: domain turn, mapper, repository, Postgres and back.
    [Fact]
    public async Task An_answer_with_a_tool_call_survives_the_round_trip_through_storage()
    {
        var chat = await Repo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        var answered = new Turn
        {
            Id = Guid.NewGuid(),
            Prompt = "hva er klokka?",
            SystemPrompt = "be brief",
            ModelKey = new ChatModelKey("fast"),
            ModelDisplayName = "Fast",
            StartedAt = DateTimeOffset.UtcNow,
            Status = TurnStatus.Completed,
            Answer =
            [
                new ToolSegment(Guid.NewGuid(), 0, "call-1", "get_current_time_utc", JsonSerializer.SerializeToElement(new { }), ToolStatus.Completed, "12:00"),
                new TextSegment(Guid.NewGuid(), 1, "Den er tolv.")
            ]
        };

        await Repo.InsertTurnAsync(OwnerId, chat.Id, ChatTurnMapper.ToEntity(answered with { Answer = [], Status = TurnStatus.Running }));
        await Repo.UpdateTurnAsync(OwnerId, chat.Id, ChatTurnMapper.ToEntity(answered));
        var reloaded = await Repo.GetChatAsync(OwnerId, chat.Id);
        var restored = ChatTurnMapper.FromEntity(Assert.Single(reloaded!.Turns), key => key.Value, NullLogger.Instance);

        var tool = Assert.IsType<ToolSegment>(restored.Answer[0]);
        Assert.Equal(("call-1", ToolStatus.Completed, "12:00"), (tool.CallId, tool.Status, tool.Result));
        Assert.Equal("Den er tolv.", Assert.IsType<TextSegment>(restored.Answer[1]).Text);
    }

    private static ChatTurn Row(
        string prompt,
        DateTimeOffset startedAt = default,
        Guid? id = null,
        string status = "Running",
        string answerJson = "[]",
        long? durationMs = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        StartedAt = startedAt == default ? DateTimeOffset.UtcNow : startedAt,
        Prompt = prompt,
        SystemPrompt = "be brief",
        ModelKey = "fast",
        Status = status,
        AnswerJson = answerJson,
        DurationMs = durationMs
    };
}
