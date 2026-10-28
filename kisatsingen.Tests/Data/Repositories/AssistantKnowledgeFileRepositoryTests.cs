using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

// An assistant's files are reached through who may use the assistant, not
// through who owns the file: an organization-wide assistant's files are its
// owner's, and everyone uses them.
[Collection(PostgresCollection.Name)]
public sealed class AssistantKnowledgeFileRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int DefaultTokenCap = 500_000;

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private KnowledgeFileRepository Repo => new(Factory, DefaultTokenCap);

    private AssistantRepository AssistantRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_organization_wide_assistants_files_are_listed_for_everyone()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Felles", null, AssistantVisibility.Organization);
        await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        var listed = await Repo.ListFilesForAssistantAsync(OtherUserId, assistant.Id);

        Assert.Equal(["rundskriv.md"], listed.Select(file => file.FileName));
    }

    [Fact]
    public async Task A_private_assistants_files_are_listed_for_no_one_else()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Privat", null);
        await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        var listed = await Repo.ListFilesForAssistantAsync(OtherUserId, assistant.Id);

        Assert.Empty(listed);
    }

    // As when an administrator adds a file to someone else's assistant.
    [Fact]
    public async Task A_file_added_to_someone_elses_organization_wide_assistant_is_owned_by_the_assistants_owner()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Felles", null, AssistantVisibility.Organization);

        var added = await AddAsync(OtherUserId, assistant.Id, "rundskriv.md");

        Assert.Equal(OwnerId, await StoredOwnerIdAsync(added.Id));
    }

    [Fact]
    public async Task Adding_a_file_to_someone_elses_private_assistant_throws_and_leaves_nothing_behind()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Privat", null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.CreateFileForAssistantAsync(OtherUserId, assistant.Id, KnowledgeFileFactory.NewFile("rundskriv.md")));

        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task A_file_already_in_the_assistant_is_rejected_as_a_duplicate()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        var result = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.NewFile("rundskriv.md"));

        Assert.Equal(KnowledgeFileSaveRejections.DuplicateInAssistant, Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task A_full_assistant_rejects_another_file()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        await FillAsync(assistant.Id, fileCount: 20);

        var result = await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, NumberedFile(21));

        Assert.Equal(KnowledgeFileSaveRejections.AssistantFull, Assert.IsType<KnowledgeFileSaveResult.Rejected>(result).Reason);
        Assert.Equal(20, await CountFilesAsync());
    }

    // Without the assistant's row locked before counting, several would see 19.
    // Ten at once, because two rarely overlap enough to show it.
    [Fact]
    public async Task Files_added_at_once_to_an_assistant_one_short_of_full_save_only_one()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        await FillAsync(assistant.Id, fileCount: 19);

        var results = await Task.WhenAll(Enumerable.Range(20, 10)
            .Select(number => Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, NumberedFile(number))));

        Assert.Single(results.OfType<KnowledgeFileSaveResult.Saved>());
    }

    [Fact]
    public async Task Deleting_a_file_from_an_assistant_removes_it()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        var added = await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        var isDeleted = await Repo.DeleteAssistantFileAsync(OwnerId, assistant.Id, added.Id);

        Assert.True(isDeleted);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task A_file_in_someone_elses_private_assistant_is_not_deleted()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Privat", null);
        var added = await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        var isDeleted = await Repo.DeleteAssistantFileAsync(OtherUserId, assistant.Id, added.Id);

        Assert.False(isDeleted);
        Assert.Equal(1, await CountFilesAsync());
    }

    // Access is checked on the assistant given, so the file must be in it.
    [Fact]
    public async Task A_file_is_not_deleted_through_another_assistant()
    {
        var privateAssistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "Privat", null);
        var organizationWideAssistant = await AssistantRepo.CreateAssistantAsync(OtherUserId, "Felles", null, AssistantVisibility.Organization);
        var added = await AddAsync(OwnerId, privateAssistant.Id, "rundskriv.md");

        var isDeleted = await Repo.DeleteAssistantFileAsync(OtherUserId, organizationWideAssistant.Id, added.Id);

        Assert.False(isDeleted);
    }

    [Fact]
    public async Task Deleting_an_assistant_takes_its_files_with_it()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        await AddAsync(OwnerId, assistant.Id, "rundskriv.md");

        await AssistantRepo.DeleteAssistantAsync(OwnerId, assistant.Id);

        Assert.Equal(0, await CountFilesAsync());
    }

    private async Task<KnowledgeFileMetadata> AddAsync(Guid userId, Guid assistantId, string fileName)
    {
        var result = await Repo.CreateFileForAssistantAsync(userId, assistantId, KnowledgeFileFactory.NewFile(fileName));
        return Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File;
    }

    private async Task FillAsync(Guid assistantId, int fileCount)
    {
        for (var number = 1; number <= fileCount; number++)
        {
            Assert.IsType<KnowledgeFileSaveResult.Saved>(await Repo.CreateFileForAssistantAsync(OwnerId, assistantId, NumberedFile(number)));
        }
    }

    // Distinct content, so no file is a duplicate of another.
    private static NewKnowledgeFile NumberedFile(int number) =>
        KnowledgeFileFactory.NewFile($"fil-{number}.md", $"# Fil {number}\n");

    private async Task<Guid> StoredOwnerIdAsync(Guid fileId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.KnowledgeFiles.Where(f => f.Id == fileId).Select(f => f.OwnerId).SingleAsync();
    }

    private async Task<int> CountFilesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.KnowledgeFiles.CountAsync();
    }
}
