using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class KnowledgeFileRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    // Postgres SQLSTATE codes. Asserting on the class of failure rather than on
    // a message keeps these tests from breaking on a Postgres upgrade.
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private KnowledgeFileRepository Repo => new(Factory);

    private AssistantRepository AssistantRepo => new(Factory);

    private ChatRepository ChatRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateFileForAssistantAsync_numbers_chunks_from_the_order_they_arrive_in()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");

        var file = await Repo.CreateFileForAssistantAsync(
            OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "first", "second", "third"));

        await using var db = await Factory.CreateDbContextAsync();
        var contentsBySequence = await db.KnowledgeFileChunks
            .Where(c => c.KnowledgeFileId == file.Id)
            .OrderBy(c => c.Sequence)
            .Select(c => c.Content)
            .ToListAsync();

        Assert.Equal(["first", "second", "third"], contentsBySequence);
    }

    [Fact]
    public async Task CreateFileForAssistantAsync_derives_the_totals_from_the_chunks_it_stored()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");

        await Repo.CreateFileForAssistantAsync(
            OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "aaa", "bb"));

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.KnowledgeFiles.SingleAsync();

        Assert.Equal(2, stored.ChunkCount);
        Assert.Equal(5, stored.EstimatedTokenCount);
    }

    [Fact]
    public async Task CreateFileForAssistantAsync_refuses_a_draft_with_no_chunks()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("empty.pdf")));
    }

    [Fact]
    public async Task CreateFileForAssistantAsync_throws_when_the_assistant_belongs_to_another_owner()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a")));
    }

    [Fact]
    public async Task CreateFileForChatAsync_throws_when_the_chat_belongs_to_another_owner()
    {
        var chat = await ChatRepo.CreateChatAsync(OtherOwnerId, "theirs", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.Draft("doc.pdf", "a")));
    }

    [Fact]
    public async Task A_failed_create_leaves_no_partial_file_behind()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.CreateFileForAssistantAsync(OwnerId, Guid.NewGuid(), KnowledgeFileFactory.Draft("doc.pdf", "a")));

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.KnowledgeFiles.CountAsync());
        Assert.Equal(0, await db.KnowledgeFileChunks.CountAsync());
    }

    [Fact]
    public async Task Deleting_an_assistant_takes_its_files_and_their_chunks_with_it()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a", "b"));

        await AssistantRepo.DeleteAssistantAsync(OwnerId, assistant.Id);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.KnowledgeFiles.CountAsync());
        Assert.Equal(0, await db.KnowledgeFileChunks.CountAsync());
    }

    // DeleteChatAsync uses ExecuteDeleteAsync, which never loads an entity and so
    // cannot apply an EF-side delete behaviour. If the foreign key were left at
    // EF's default for an optional relationship it would emit SET NULL, which the
    // scope check constraint rejects — this test is what catches that.
    [Fact]
    public async Task Deleting_a_chat_takes_its_files_and_their_chunks_with_it()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, "hello", assistantId: null);
        await Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.Draft("doc.pdf", "a", "b"));

        var deleted = await ChatRepo.DeleteChatAsync(OwnerId, chat.Id);

        Assert.True(deleted);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.KnowledgeFiles.CountAsync());
        Assert.Equal(0, await db.KnowledgeFileChunks.CountAsync());
    }

    [Fact]
    public async Task DeleteFileAsync_takes_the_chunks_with_it()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a", "b"));

        var deleted = await Repo.DeleteFileAsync(OwnerId, file.Id);

        Assert.True(deleted);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.KnowledgeFileChunks.CountAsync());
    }

    [Fact]
    public async Task DeleteFileAsync_reports_false_for_another_owners_file()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OtherOwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a"));

        var deleted = await Repo.DeleteFileAsync(OwnerId, file.Id);

        Assert.False(deleted);
    }

    [Fact]
    public async Task GetChunksAsync_returns_the_requested_span_in_document_order()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(
            OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "zero", "one", "two", "three"));

        var chunks = await Repo.GetChunksAsync(OwnerId, file.Id, firstSequence: 1, lastSequence: 2);

        Assert.Equal(["one", "two"], chunks.Select(c => c.Content));
    }

    [Fact]
    public async Task GetChunksAsync_returns_nothing_for_another_owners_file()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OtherOwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "secret"));

        var chunks = await Repo.GetChunksAsync(OwnerId, file.Id, firstSequence: 0, lastSequence: 0);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task GetChunksAsync_refuses_a_span_wider_than_the_limit()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Repo.GetChunksAsync(OwnerId, file.Id, 0, KnowledgeFileRepository.MaxChunkSpan));
    }

    // int.MaxValue is what a model asking for "the whole document" looks like.
    // Computed in int arithmetic the span wraps negative and sails past the cap.
    [Fact]
    public async Task GetChunksAsync_refuses_a_span_wide_enough_to_overflow_the_cap_check()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a", "b"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Repo.GetChunksAsync(OwnerId, file.Id, firstSequence: 0, lastSequence: int.MaxValue));
    }

    [Fact]
    public async Task GetChunksAsync_refuses_a_range_that_ends_before_it_starts()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Repo.GetChunksAsync(OwnerId, file.Id, firstSequence: 5, lastSequence: 2));
    }

    [Fact]
    public async Task ListFilesForAssistantAsync_returns_only_the_callers_files()
    {
        var mine = await AssistantRepo.CreateAssistantAsync(OwnerId, "Mine", null, "x");
        var theirs = await AssistantRepo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");
        await Repo.CreateFileForAssistantAsync(OwnerId, mine.Id, KnowledgeFileFactory.Draft("mine.pdf", "a"));
        await Repo.CreateFileForAssistantAsync(OtherOwnerId, theirs.Id, KnowledgeFileFactory.Draft("theirs.pdf", "a"));

        var summaries = await Repo.ListFilesForAssistantAsync(OwnerId, mine.Id);

        Assert.Equal(["mine.pdf"], summaries.Select(s => s.FileName));
    }

    // The repository's two create methods cannot express either of the next two
    // states, which is the point of splitting them. These reach past the
    // repository to prove the database refuses them anyway.
    [Fact]
    public async Task The_database_rejects_a_file_scoped_to_neither_an_assistant_nor_a_chat()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(
            () => InsertFileDirectlyAsync(assistantId: null, chatId: null));

        Assert.Equal(CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task The_database_rejects_a_file_scoped_to_both_an_assistant_and_a_chat()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var chat = await ChatRepo.CreateChatAsync(OwnerId, "hello", assistantId: null);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => InsertFileDirectlyAsync(assistant.Id, chat.Id));

        Assert.Equal(CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task The_database_rejects_two_chunks_claiming_the_same_position()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null, "x");
        var file = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("doc.pdf", "a"));

        await using var db = await Factory.CreateDbContextAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "KnowledgeFileChunks"
                ("Id", "KnowledgeFileId", "Sequence", "Heading", "Content", "EstimatedTokenCount")
            VALUES ({0}, {1}, 0, NULL, 'duplicate', 1)
            """,
            Guid.NewGuid(), file.Id));

        Assert.Equal(UniqueViolation, error.SqlState);
    }

    private async Task InsertFileDirectlyAsync(Guid? assistantId, Guid? chatId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "KnowledgeFiles"
                ("Id", "OwnerId", "AssistantId", "ChatId", "FileName", "ContentType",
                 "SizeBytes", "Sha256", "Summary", "ChunkCount", "EstimatedTokenCount", "CreatedAt")
            VALUES (@id, @ownerId, @assistantId, @chatId, 'doc.pdf', 'application/pdf', 1024, 'abc', 'summary', 1, 1, now())
            """,
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("ownerId", OwnerId),
            UuidParameter("assistantId", assistantId),
            UuidParameter("chatId", chatId));
    }

    // EF's raw-SQL builder has no store type mapping for DBNull, so a null scope
    // column cannot be passed as a bare array element — and a NULL is exactly
    // what these two tests are about.
    private static NpgsqlParameter UuidParameter(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = (object?)value ?? DBNull.Value };
}
