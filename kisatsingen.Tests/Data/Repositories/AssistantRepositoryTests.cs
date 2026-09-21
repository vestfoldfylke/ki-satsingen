using kisatsingen.Data;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class AssistantRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OwnerId = "Whatever";
    private const string OtherOwnerId = "SomebodyElse";

    private IDbContextFactory<AppDbContext> Factory => fixture.Factory;

    private AssistantRepository Repo => new(Factory);

    private ChatRepository ChatRepo => new(Factory);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAssistantAsync_stores_the_assistant_with_an_assigned_id()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", "Hjelper med saker", "Du er hjelpsom.");

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Assistants.SingleAsync();

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(assistant.Id, stored.Id);
        Assert.Equal("Du er hjelpsom.", stored.Instructions);
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_a_blank_name()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, "   ", null, "x"));
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_a_name_the_column_cannot_hold()
    {
        var tooLong = new string('a', Assistant.MaxNameLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, tooLong, null, "x"));
    }

    [Fact]
    public async Task CreateAssistantAsync_stores_a_blank_description_as_absent()
    {
        await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", "   ", "x");

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Assistants.SingleAsync();

        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_blank_instructions()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, "   "));
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_instructions_the_column_cannot_hold()
    {
        var tooLong = new string('a', Assistant.MaxInstructionsLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, tooLong));
    }

    [Fact]
    public async Task UpdateAssistantAsync_refuses_a_blank_name()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, "x");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.UpdateAssistantAsync(OwnerId, assistant.Id, "  ", null, "x"));
    }

    [Fact]
    public async Task UpdateAssistantAsync_refuses_blank_instructions()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, "x");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.UpdateAssistantAsync(OwnerId, assistant.Id, "Saksbehandler", null, "   "));
    }

    [Fact]
    public async Task ListAssistantsAsync_returns_only_the_callers_assistants()
    {
        await Repo.CreateAssistantAsync(OwnerId, "Mine", null, "x");
        await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");

        var summaries = await Repo.ListAssistantsAsync(OwnerId);

        Assert.Equal(["Mine"], summaries.Select(s => s.Name));
    }

    [Fact]
    public async Task GetAssistantAsync_returns_null_for_another_owners_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");

        var found = await Repo.GetAssistantAsync(OwnerId, assistant.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateAssistantAsync_throws_for_another_owners_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo.UpdateAssistantAsync(OwnerId, assistant.Id, "Hijacked", null, "evil"));
    }

    [Fact]
    public async Task DeleteAssistantAsync_reports_false_for_another_owners_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "x");

        var deleted = await Repo.DeleteAssistantAsync(OwnerId, assistant.Id);

        Assert.False(deleted);
    }

    [Fact]
    public async Task Deleting_an_assistant_leaves_its_chats_alive_without_the_link()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, "x");
        var chat = await ChatRepo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        await Repo.DeleteAssistantAsync(OwnerId, assistant.Id);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Null(stored.AssistantId);
    }

    [Fact]
    public async Task CreateChatAsync_throws_when_the_assistant_belongs_to_another_owner()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null, "secret instructions");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ChatRepo.CreateChatAsync(OwnerId, "hello", assistant.Id));
    }

    [Fact]
    public async Task GetAssistantAsync_includes_file_metadata_but_not_chunk_content()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null, "x");
        var files = new KnowledgeFileRepository(Factory, maxEstimatedTokenCount: 500_000);
        await files.CreateFileForAssistantAsync(OwnerId, assistant.Id, KnowledgeFileFactory.Draft("rundskriv.pdf", "a", "b"));

        var found = await Repo.GetAssistantAsync(OwnerId, assistant.Id);

        var file = Assert.Single(found!.KnowledgeFiles);
        Assert.Equal("rundskriv.pdf", file.FileName);
        Assert.Empty(file.Chunks);
    }
}
