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
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", "Hjelper med saker");

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Assistants.SingleAsync();

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(assistant.Id, stored.Id);
    }

    [Fact]
    public async Task CreateAssistantAsync_creates_the_assistant_without_instructions()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);

        Assert.Null(assistant.Instructions);
    }

    [Fact]
    public async Task CreateAssistantAsync_makes_the_assistant_private_unless_told_otherwise()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);

        Assert.Equal(AssistantVisibility.Private, assistant.Visibility);
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_a_blank_name()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, "   ", null));
    }

    [Fact]
    public async Task CreateAssistantAsync_refuses_a_name_the_column_cannot_hold()
    {
        var tooLong = new string('a', Assistant.MaxNameLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.CreateAssistantAsync(OwnerId, tooLong, null));
    }

    [Fact]
    public async Task CreateAssistantAsync_stores_a_blank_description_as_absent()
    {
        await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", "   ");

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Assistants.SingleAsync();

        Assert.Null(stored.Description);
    }

    [Fact]
    public async Task UpdateAssistantDetailsAsync_stores_a_changed_visibility()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);

        await Repo.UpdateAssistantDetailsAsync(OwnerId, assistant.Id, "Saksbehandler", null, AssistantVisibility.Organization);

        var stored = await Repo.GetAssistantAsync(OwnerId, assistant.Id);
        Assert.Equal(AssistantVisibility.Organization, stored?.Visibility);
    }

    [Fact]
    public async Task UpdateAssistantDetailsAsync_refuses_a_blank_name()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.UpdateAssistantDetailsAsync(OwnerId, assistant.Id, "  ", null, AssistantVisibility.Private));
    }

    // Saving the details must not overwrite instructions someone else just saved.
    [Fact]
    public async Task UpdateAssistantDetailsAsync_leaves_the_instructions_as_they_are()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);
        await Repo.UpdateAssistantInstructionsAsync(OwnerId, assistant.Id, "Du er hjelpsom.");

        await Repo.UpdateAssistantDetailsAsync(OwnerId, assistant.Id, "Ny saksbehandler", null, AssistantVisibility.Private);

        var stored = await Repo.GetAssistantAsync(OwnerId, assistant.Id);
        Assert.Equal("Du er hjelpsom.", stored?.Instructions);
    }

    [Fact]
    public async Task UpdateAssistantInstructionsAsync_stores_the_instructions()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);

        await Repo.UpdateAssistantInstructionsAsync(OwnerId, assistant.Id, "Du er hjelpsom.");

        var stored = await Repo.GetAssistantAsync(OwnerId, assistant.Id);
        Assert.Equal("Du er hjelpsom.", stored?.Instructions);
    }

    [Fact]
    public async Task UpdateAssistantInstructionsAsync_clears_the_instructions_when_given_blank()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);
        await Repo.UpdateAssistantInstructionsAsync(OwnerId, assistant.Id, "Du er hjelpsom.");

        await Repo.UpdateAssistantInstructionsAsync(OwnerId, assistant.Id, "   ");

        var stored = await Repo.GetAssistantAsync(OwnerId, assistant.Id);
        Assert.Null(stored?.Instructions);
    }

    [Fact]
    public async Task UpdateAssistantInstructionsAsync_refuses_instructions_the_column_cannot_hold()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);
        var tooLong = new string('a', Assistant.MaxInstructionsLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Repo.UpdateAssistantInstructionsAsync(OwnerId, assistant.Id, tooLong));
    }

    [Fact]
    public async Task ListAssistantsAsync_returns_the_users_own_and_organization_wide_assistants_but_not_another_owners_private_ones()
    {
        await Repo.CreateAssistantAsync(OwnerId, "Mine", null);
        await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null);
        await Repo.CreateAssistantAsync(OtherOwnerId, "OrganizationWide", null, AssistantVisibility.Organization);

        var summaries = await Repo.ListAssistantsAsync(OwnerId);

        Assert.Equal(["Mine", "OrganizationWide"], summaries.Select(s => s.Name).Order());
    }

    [Fact]
    public async Task GetAssistantAsync_returns_null_for_another_owners_private_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null);

        var found = await Repo.GetAssistantAsync(OwnerId, assistant.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAssistantAsync_returns_another_owners_organization_wide_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "OrganizationWide", null, AssistantVisibility.Organization);

        var found = await Repo.GetAssistantAsync(OwnerId, assistant.Id);

        Assert.Equal(assistant.Id, found?.Id);
    }

    [Fact]
    public async Task UpdateAssistantDetailsAsync_reports_false_for_another_owners_private_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null);

        var updated = await Repo.UpdateAssistantDetailsAsync(OwnerId, assistant.Id, "Hijacked", null, AssistantVisibility.Private);

        Assert.False(updated);
    }

    // The query only guarantees the user may use it; whether they may edit it
    // is the CanEditAssistant policy, checked before this is called.
    [Fact]
    public async Task UpdateAssistantDetailsAsync_reaches_another_owners_organization_wide_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "OrganizationWide", null, AssistantVisibility.Organization);

        var updated = await Repo.UpdateAssistantDetailsAsync(OwnerId, assistant.Id, "Renamed", null, AssistantVisibility.Organization);

        Assert.True(updated);
    }

    [Fact]
    public async Task DeleteAssistantAsync_reports_false_for_another_owners_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null);

        var deleted = await Repo.DeleteAssistantAsync(OwnerId, assistant.Id);

        Assert.False(deleted);
    }

    [Fact]
    public async Task Deleting_an_assistant_leaves_its_chats_alive_without_the_link()
    {
        var assistant = await Repo.CreateAssistantAsync(OwnerId, "Saksbehandler", null);
        var chat = await ChatRepo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        await Repo.DeleteAssistantAsync(OwnerId, assistant.Id);

        await using var db = await Factory.CreateDbContextAsync();
        var stored = await db.Chats.SingleAsync(c => c.Id == chat.Id);
        Assert.Null(stored.AssistantId);
    }

    [Fact]
    public async Task CreateChatAsync_throws_when_the_assistant_is_another_owners_private_one()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "Theirs", null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ChatRepo.CreateChatAsync(OwnerId, "hello", assistant.Id));
    }

    [Fact]
    public async Task CreateChatAsync_accepts_another_owners_organization_wide_assistant()
    {
        var assistant = await Repo.CreateAssistantAsync(OtherOwnerId, "OrganizationWide", null, AssistantVisibility.Organization);

        var chat = await ChatRepo.CreateChatAsync(OwnerId, "hello", assistant.Id);

        Assert.Equal(assistant.Id, chat.AssistantId);
    }
}
