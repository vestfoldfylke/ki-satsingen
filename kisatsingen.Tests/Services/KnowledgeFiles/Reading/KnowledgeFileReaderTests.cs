using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Reading;
using kisatsingen.Tests.Data;
using kisatsingen.Tests.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Reading;

// A file id doesn't say whose file it is; the reader finds it among the
// assistant's files and the chat's. Access itself is the repository's, tested there.
[Collection(PostgresCollection.Name)]
public sealed class KnowledgeFileReaderTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int DefaultTokenCap = 500_000;

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private KnowledgeFileRepository Repo => new(Factory, DefaultTokenCap);

    private KnowledgeFileReader Reader => new(Repo);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_assistants_file_is_read_through_a_chat_that_uses_the_assistant()
    {
        var (scope, assistantId) = await CreateChatWithAssistantAsync();
        var result = await Repo.CreateFileForAssistantAsync(OwnerId, assistantId, KnowledgeFileFactory.NewFile("rundskriv.md"));
        var file = Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File;

        var text = await Reader.ReadFileTextAsync(scope, file.Id, CancellationToken.None);

        Assert.Equal("rundskriv.md", text?.File.FileName);
    }

    [Fact]
    public async Task A_chats_own_file_is_read_in_a_chat_with_an_assistant()
    {
        var (scope, _) = await CreateChatWithAssistantAsync();
        var result = await Repo.CreateFileForChatAsync(OwnerId, scope.ChatId, KnowledgeFileFactory.NewFile("notat.md"));
        var file = Assert.IsType<KnowledgeFileSaveResult.Saved>(result).File;

        var text = await Reader.ReadFileTextAsync(scope, file.Id, CancellationToken.None);

        Assert.Equal("notat.md", text?.File.FileName);
    }

    private async Task<(KnowledgeFileScope Scope, Guid AssistantId)> CreateChatWithAssistantAsync()
    {
        var assistant = await new AssistantRepository(Factory).CreateAssistantAsync(OwnerId, "A", null);
        var chat = await new ChatRepository(Factory).CreateChatAsync(OwnerId, Guid.NewGuid(), "hei", assistant.Id);
        return (new KnowledgeFileScope(OwnerId, chat.Id, assistant.Id), assistant.Id);
    }
}
