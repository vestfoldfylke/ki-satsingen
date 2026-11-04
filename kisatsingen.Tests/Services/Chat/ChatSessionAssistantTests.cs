using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionAssistantTests
{
    private const string AssistantInstructions = "Svar kort og formelt.";

    [Fact]
    public async Task A_new_chat_with_an_assistant_is_sent_the_assistants_instructions()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.StartNewAssistantChatAsync(NewAssistant(), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.EndsWith(AssistantInstructions, harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task A_new_chat_with_an_assistant_is_saved_with_that_assistant()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.Equal(assistant.Id, Assert.Single(harness.Repository.CreatedChats).AssistantId);
    }

    [Fact]
    public async Task An_opened_chat_with_an_assistant_is_sent_the_assistants_instructions()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        await harness.Session.SendAsync("hei");

        Assert.EndsWith(AssistantInstructions, harness.Client.LastOptions?.Instructions);
    }

    // The chat goes on as a plain chat, without the assistant's instructions.
    [Fact]
    public async Task An_opened_chat_whose_assistant_is_no_longer_usable_is_sent_the_plain_chat_instructions()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.Assistants.RemoveAccess(assistant.Id);
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        await harness.Session.SendAsync("hei");

        Assert.Equal(ChatInstructions.ForPlainChat, harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task A_chat_is_sent_the_instructions_of_its_assistant_as_updated()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: []);
        harness.Session.UpdateAssistant(NewAssistant(instructions: "Svar på nynorsk.", id: assistant.Id), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.EndsWith("Svar på nynorsk.", harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task A_chat_without_an_assistant_is_not_given_the_one_that_was_updated()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.StartNewPlainChatAsync();
        harness.Session.UpdateAssistant(NewAssistant(), assistantFiles: []);

        await harness.Session.SendAsync("hei");

        Assert.Equal(ChatInstructions.ForPlainChat, harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task An_opened_chat_shows_its_assistants_current_name()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id, nameSnapshot: "Gammelt navn").Id);

        Assert.Equal("Saksbehandler", harness.Session.AssistantName);
    }

    [Fact]
    public async Task An_opened_chat_whose_assistant_is_no_longer_usable_shows_the_name_it_was_saved_with()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.Assistants.RemoveAccess(assistant.Id);

        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id, nameSnapshot: "Gammelt navn").Id);

        Assert.Equal("Gammelt navn", harness.Session.AssistantName);
    }

    [Fact]
    public async Task An_opened_chat_links_to_its_assistant()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());

        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        Assert.Equal(assistant.Id, harness.Session.AssistantId);
    }

    [Fact]
    public async Task An_opened_chat_whose_assistant_is_no_longer_usable_does_not_link_to_it()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.Assistants.RemoveAccess(assistant.Id);

        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        Assert.Null(harness.Session.AssistantId);
    }

    [Fact]
    public async Task A_new_chat_shows_no_name_from_the_chat_before_it()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.Assistants.RemoveAccess(assistant.Id);
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id, nameSnapshot: "Gammelt navn").Id);

        await harness.Session.StartNewPlainChatAsync();

        Assert.Null(harness.Session.AssistantName);
    }

    [Fact]
    public async Task The_estimated_context_counts_the_assistants_instructions()
    {
        await using var shortInstructionsChat = new ChatSessionHarness();
        await using var longInstructionsChat = new ChatSessionHarness();
        await shortInstructionsChat.Session.StartNewAssistantChatAsync(NewAssistant(instructions: "i"), assistantFiles: []);
        await longInstructionsChat.Session.StartNewAssistantChatAsync(NewAssistant(instructions: new string('i', 3_000)), assistantFiles: []);

        await shortInstructionsChat.Session.SendAsync("hei");
        await longInstructionsChat.Session.SendAsync("hei");

        Assert.True(longInstructionsChat.Session.EstimatedContextTokens > shortInstructionsChat.Session.EstimatedContextTokens);
    }

    [Fact]
    public async Task A_new_chat_with_an_assistant_that_has_files_gets_the_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        var file = harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: [file]);

        await harness.Session.SendAsync("hei");

        Assert.Contains("read_file", harness.Client.LastToolNames);
    }

    [Fact]
    public async Task A_new_chat_with_an_assistant_that_has_files_is_told_about_them()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        var file = harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: [file]);

        await harness.Session.SendAsync("hei");

        Assert.Contains(ChatInstructions.AssistantFilesLine, harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task An_opened_chat_whose_assistant_has_files_gets_the_file_tools()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        await harness.Session.SendAsync("hei");

        Assert.Contains("read_file", harness.Client.LastToolNames);
    }

    [Fact]
    public async Task An_opened_chat_whose_assistant_is_no_longer_usable_gets_no_file_tools_for_its_files()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        harness.Assistants.RemoveAccess(assistant.Id);
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        await harness.Session.SendAsync("hei");

        Assert.DoesNotContain("read_file", harness.Client.LastToolNames);
    }

    [Fact]
    public async Task A_chat_gets_the_file_tools_once_its_assistant_has_its_first_file()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        await harness.Session.StartNewAssistantChatAsync(assistant, assistantFiles: []);
        var file = harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        harness.Session.UpdateAssistant(assistant, assistantFiles: [file]);

        await harness.Session.SendAsync("hei");

        Assert.Contains("read_file", harness.Client.LastToolNames);
    }

    // A chat that isn't found sets no files of its own, so this shows that
    // leaving the previous chat cleared them.
    [Fact]
    public async Task A_chat_that_is_not_found_shows_none_of_the_assistant_files_from_the_chat_before()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = harness.Assistants.Store(NewAssistant());
        harness.KnowledgeFiles.AddAssistantFile(assistant.Id, "rundskriv.md");
        await harness.Session.OpenChatAsync(StoreChatWith(harness, assistant.Id).Id);

        await harness.Session.OpenChatAsync(Guid.NewGuid());

        Assert.Empty(harness.Session.AssistantFiles);
    }

    private static Assistant NewAssistant(string instructions = AssistantInstructions, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Name = "Saksbehandler",
        Instructions = instructions,
        Visibility = AssistantVisibility.Private
    };

    private static ChatEntity StoreChatWith(ChatSessionHarness harness, Guid assistantId, string nameSnapshot = "Saksbehandler") =>
        harness.Repository.Store(new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            AssistantId = assistantId,
            AssistantNameSnapshot = nameSnapshot,
            Title = "stored",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
}
