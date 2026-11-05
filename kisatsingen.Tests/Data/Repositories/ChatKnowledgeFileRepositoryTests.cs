using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

// A chat's files are reached through the chat's owner, and only through that chat.
[Collection(PostgresCollection.Name)]
public sealed class ChatKnowledgeFileRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int DefaultTokenCap = 500_000;

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private KnowledgeFileRepository Repo => new(Factory, DefaultTokenCap);

    private AssistantRepository AssistantRepo => new(Factory);

    private ChatRepository ChatRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_file_from_a_chat_removes_it()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hei", assistantId: null);
        var added = await AddAsync(OwnerId, chat.Id, "notat.md");

        var isDeleted = await Repo.DeleteChatFileAsync(OwnerId, chat.Id, added.Id);

        Assert.True(isDeleted);
        Assert.Equal(0, await CountFilesAsync());
    }

    [Fact]
    public async Task A_file_in_someone_elses_chat_is_not_deleted()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hei", assistantId: null);
        var added = await AddAsync(OwnerId, chat.Id, "notat.md");

        var isDeleted = await Repo.DeleteChatFileAsync(OtherUserId, chat.Id, added.Id);

        Assert.False(isDeleted);
        Assert.Equal(1, await CountFilesAsync());
    }

    // Ownership is checked on the chat given, so the file must be in it.
    [Fact]
    public async Task A_file_is_not_deleted_through_another_chat()
    {
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hei", assistantId: null);
        var otherUsersChat = await ChatRepo.CreateChatAsync(OtherUserId, Guid.NewGuid(), "hallo", assistantId: null);
        var added = await AddAsync(OwnerId, chat.Id, "notat.md");

        var isDeleted = await Repo.DeleteChatFileAsync(OtherUserId, otherUsersChat.Id, added.Id);

        Assert.False(isDeleted);
        Assert.Equal(1, await CountFilesAsync());
    }

    [Fact]
    public async Task An_assistants_file_is_not_deleted_through_a_chat_with_that_assistant()
    {
        var assistant = await AssistantRepo.CreateAssistantAsync(OwnerId, "A", null);
        var chat = await ChatRepo.CreateChatAsync(OwnerId, Guid.NewGuid(), "hei", assistant.Id);
        var added = Assert.IsType<KnowledgeFileSaveResult.Saved>(
            await Repo.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.NewFile("rundskriv.md"))).File;

        var isDeleted = await Repo.DeleteChatFileAsync(OwnerId, chat.Id, added.Id);

        Assert.False(isDeleted);
        Assert.Equal(1, await CountFilesAsync());
    }

    private async Task<KnowledgeFileMetadata> AddAsync(Guid userId, Guid chatId, string fileName)
    {
        var result = await Repo.CreateFileForChatAsync(userId, chatId, KnowledgeFileFactory.NewFile(fileName));
        return Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File;
    }

    private async Task<int> CountFilesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.KnowledgeFiles.CountAsync();
    }
}
