using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class KnowledgeFileRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int DefaultTokenCap = 500_000;

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private KnowledgeFileRepository Repo => new(Factory, DefaultTokenCap);

    private KnowledgeFileRepository RepoWithTokenCap(int maxEstimatedTokenCount) =>
        new(Factory, maxEstimatedTokenCount);

    private AssistantRepository AssistantRepo => new(Factory);

    private ChatRepository ChatRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_file_saved_into_a_chat_reads_back_with_the_metadata_it_was_saved_with()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));
        var found = await Repo.GetFileAsync(OwnerId, saved.Id);

        Assert.Equal(saved, found);
    }

    [Fact]
    public async Task A_saved_file_keeps_its_markdown_and_content_origin()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var newFile = KnowledgeFileFactory.NewFile("bilde.png", "Bilde: 10 x 10 px.") with { Origin = ContentOrigin.ImagePlaceholder };

        var saved = await SaveIntoChatAsync(chat.Id, newFile);

        var stored = await LoadStoredAsync(saved.Id);
        Assert.Equal(("Bilde: 10 x 10 px.", ContentOrigin.ImagePlaceholder), (stored.Markdown, saved.ContentOrigin));
    }

    [Fact]
    public async Task A_content_origin_this_build_does_not_know_reads_back_as_unknown_instead_of_failing_the_list()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        await InsertFileDirectlyAsync(assistantId: null, chat.Id, contentOrigin: "FromANewerBuild");

        var listed = await Repo.ListFilesForChatAsync(OwnerId, chat.Id);

        Assert.Null(Assert.Single(listed).ContentOrigin);
    }

    [Fact]
    public async Task A_new_file_with_an_undefined_content_origin_is_refused()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var newFile = KnowledgeFileFactory.NewFile("notat.md") with { Origin = (ContentOrigin)99 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, newFile));
    }

    [Fact]
    public async Task The_line_count_and_token_estimate_are_derived_from_the_markdown()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        // 15 characters, so 5 estimated tokens; the final newline ends the
        // last line rather than starting a fifth.
        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md", "en\nto\ntre\nfire\n"));

        Assert.Equal((4, 5), (saved.LineCount, saved.EstimatedTokenCount));
    }

    [Fact]
    public async Task A_new_file_starts_at_version_1()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Markdown_with_crlf_and_lone_cr_is_stored_with_lf_only_and_counted_to_match()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.txt", "ett\r\nto\rtre"));

        var stored = await LoadStoredAsync(saved.Id);
        Assert.Equal(("ett\nto\ntre", 3), (stored.Markdown, stored.LineCount));
    }

    [Fact]
    public async Task A_file_without_a_summary_is_stored_without_one()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md") with { Summary = null });

        Assert.Null(saved.Summary);
    }

    [Fact]
    public async Task Markdown_over_the_token_cap_is_rejected_and_nothing_is_stored()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        // 33 characters is 11 estimated tokens, one over the cap.
        var newFile = KnowledgeFileFactory.NewFile("stor.md", new string('a', 33));

        var result = await RepoWithTokenCap(10).CreateFileForChatAsync(OwnerId, chat.Id, newFile);

        Assert.Equal(ConversionRejections.TooManyTokens(10), Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task Markdown_with_no_text_is_rejected_and_nothing_is_stored()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var result = await Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.NewFile("tom.md", " \r\n\t\n"));

        Assert.Equal(ConversionRejections.Empty, Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task Markdown_containing_nul_is_rejected_and_nothing_is_stored()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var result = await Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.NewFile("notat.md", "før\0etter"));

        Assert.Equal(ConversionRejections.InvalidCharacters, Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task A_file_already_saved_in_the_same_chat_is_rejected_as_a_duplicate()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var result = await Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.NewFile("kopi av notat.md"));

        Assert.Equal(KnowledgeFileSaveRejections.DuplicateInChat, Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
        Assert.Equal(1, await CountFilesAsync());
    }

    [Fact]
    public async Task The_same_file_is_accepted_in_a_different_chat()
    {
        var first = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "first", assistantId: null);
        var second = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "second", assistantId: null);
        await SaveIntoChatAsync(first.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var result = await Repo.CreateFileForChatAsync(OwnerId, second.Id, KnowledgeFileFactory.NewFile("notat.md"));

        Assert.IsType<KnowledgeFileSaveResult.Saved>(result);
    }

    [Fact]
    public async Task A_new_file_with_a_blank_file_name_is_refused()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.NewFile("   ")));
    }

    [Fact]
    public async Task A_new_file_with_a_malformed_sha256_is_refused()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        // 63 hex chars — right alphabet, wrong length. Also covers non-hex via the length branch.
        var newFile = KnowledgeFileFactory.NewFile("notat.md") with { Sha256 = new string('a', 63) };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, newFile));
    }

    [Fact]
    public async Task An_uppercase_sha256_is_stored_in_lowercase()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var newFile = KnowledgeFileFactory.NewFile("notat.md") with { Sha256 = new string('A', 64) };

        var saved = await SaveIntoChatAsync(chat.Id, newFile);

        Assert.Equal(new string('a', 64), saved.Sha256);
    }

    [Fact]
    public async Task A_huge_sha256_is_not_embedded_in_the_error_message()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        // 10_000-char blob masquerading as a hash; nothing about the message
        // should carry the full input into logs.
        var giantBlob = new string('z', 10_000);
        var newFile = KnowledgeFileFactory.NewFile("notat.md") with { Sha256 = giantBlob };

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, newFile));

        Assert.DoesNotContain(giantBlob, error.Message);
        Assert.Contains("10000", error.Message);
    }

    [Fact]
    public async Task A_new_file_with_a_non_positive_page_count_is_refused()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var newFile = KnowledgeFileFactory.NewFile("notat.md") with { PageCount = 0 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, newFile));
    }

    [Fact]
    public async Task Saving_into_another_owners_chat_throws_and_leaves_nothing_behind()
    {
        var chat = await ChatRepo.CreateChatAsync(OtherOwnerId, Guid.NewGuid(), "theirs", assistantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.CreateFileForChatAsync(OwnerId, chat.Id, KnowledgeFileFactory.NewFile("notat.md")));

        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task GetFileAsync_returns_null_for_another_owners_file()
    {
        var chat = await ChatRepo.CreateChatAsync(OtherOwnerId, Guid.NewGuid(), "theirs", assistantId: null);
        var saved = await Repo.CreateFileForChatAsync(OtherOwnerId, chat.Id, KnowledgeFileFactory.NewFile("hemmelig.md"));

        var found = await Repo.GetFileAsync(OwnerId, Assert.IsType<KnowledgeFileSaveResult.Saved>(saved).File.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task Listing_a_chats_files_carries_each_files_sha256_in_the_order_they_were_saved()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var first = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("en.md", "en"));
        var second = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("to.md", "to"));

        var listed = await Repo.ListFilesForChatAsync(OwnerId, chat.Id);

        Assert.Equal([first.Sha256, second.Sha256], listed.Select(f => f.Sha256));
    }

    [Fact]
    public async Task A_chats_file_is_found_by_its_content()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var found = await Repo.FindChatFileBySha256Async(OwnerId, chat.Id, saved.Sha256);

        Assert.Equal(saved, found);
    }

    [Fact]
    public async Task Another_owners_file_is_never_found_by_its_content()
    {
        var chat = await ChatRepo.CreateChatAsync(OtherOwnerId, Guid.NewGuid(), "theirs", assistantId: null);
        var saved = Assert.IsType<KnowledgeFileSaveResult.Saved>(await Repo.CreateFileForChatAsync(OtherOwnerId, chat.Id, KnowledgeFileFactory.NewFile("hemmelig.md"))).File;

        var found = await Repo.FindChatFileBySha256Async(OwnerId, chat.Id, saved.Sha256);

        Assert.Null(found);
    }

    // Saving lowercases it, so looking up must too.
    [Fact]
    public async Task A_chats_file_is_found_by_its_content_given_in_uppercase()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var found = await Repo.FindChatFileBySha256Async(OwnerId, chat.Id, saved.Sha256.ToUpperInvariant());

        Assert.Equal(saved, found);
    }

    // Guards the DDL: DeleteChatAsync uses ExecuteDeleteAsync, so an optional FK
    // left at EF's default would emit SET NULL and break the check constraint.
    [Fact]
    public async Task Deleting_a_chat_takes_its_files_with_it()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var deleted = await ChatRepo.DeleteChatAsync(OwnerId, chat.Id);

        Assert.True(deleted);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task DeleteFileAsync_removes_the_file()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);
        var saved = await SaveIntoChatAsync(chat.Id, KnowledgeFileFactory.NewFile("notat.md"));

        var deleted = await Repo.DeleteFileAsync(OwnerId, saved.Id);

        Assert.True(deleted);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task DeleteFileAsync_reports_false_for_another_owners_file()
    {
        var chat = await ChatRepo.CreateChatAsync(OtherOwnerId, Guid.NewGuid(), "theirs", assistantId: null);
        var saved = await Repo.CreateFileForChatAsync(OtherOwnerId, chat.Id, KnowledgeFileFactory.NewFile("hemmelig.md"));

        var deleted = await Repo.DeleteFileAsync(OwnerId, Assert.IsType<KnowledgeFileSaveResult.Saved>(saved).File.Id);

        Assert.False(deleted);
    }

    // Reaches past the repository, whose two create methods cannot express these
    // states, to prove the database refuses them anyway.
    [Fact]
    public async Task The_database_rejects_a_file_scoped_to_neither_an_assistant_nor_a_chat()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(
            () => InsertFileDirectlyAsync(assistantId: null, chatId: null));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task The_database_rejects_a_file_scoped_to_both_an_assistant_and_a_chat()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hello", assistantId: null);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => InsertFileDirectlyAsync(assistant.Id, chat.Id));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private async Task<KnowledgeFileMetadata> SaveIntoChatAsync(Guid chatId, NewKnowledgeFile newFile)
    {
        var result = await Repo.CreateFileForChatAsync(OwnerId, chatId, newFile);
        return Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File;
    }

    private async Task<KnowledgeFile> LoadStoredAsync(Guid fileId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.KnowledgeFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
    }

    private async Task<int> CountFilesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.KnowledgeFiles.CountAsync();
    }

    private async Task InsertFileDirectlyAsync(Guid? assistantId, Guid? chatId, string contentOrigin = nameof(ContentOrigin.TextFile))
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "KnowledgeFiles"
                ("Id", "OwnerId", "AssistantId", "ChatId", "FileName", "ContentType", "SizeBytes", "Sha256",
                 "Markdown", "LineCount", "EstimatedTokenCount", "ContentOrigin", "Version", "CreatedAt", "UpdatedAt")
            VALUES (@id, @ownerId, @assistantId, @chatId, 'notat.md', 'text/markdown', 1024, 'abc',
                    'tekst', 1, 1, @contentOrigin, 1, now(), now())
            """,
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("ownerId", OwnerId),
            new NpgsqlParameter("contentOrigin", contentOrigin),
            UuidParameter("assistantId", assistantId),
            UuidParameter("chatId", chatId));
    }

    // EF's raw-SQL builder has no store type mapping for DBNull, so a SQL NULL
    // cannot be passed as a bare array element.
    private static NpgsqlParameter UuidParameter(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = (object?)value ?? DBNull.Value };
}
