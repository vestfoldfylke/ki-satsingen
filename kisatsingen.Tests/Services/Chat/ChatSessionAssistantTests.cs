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
        await harness.Session.StartNewChatAsync(NewAssistant());

        await harness.Session.SendAsync("hei");

        Assert.EndsWith(AssistantInstructions, harness.Client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task A_new_chat_with_an_assistant_is_saved_with_that_assistant()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();
        await harness.Session.StartNewChatAsync(assistant);

        await harness.Session.SendAsync("hei");

        Assert.Equal(assistant.Id, Assert.Single(harness.Repository.CreatedChats).AssistantId);
    }

    // The assistant page keeps an unsent chat with its assistant, and /new only
    // an unsent chat without one.
    [Fact]
    public async Task A_new_chat_with_an_assistant_is_new_only_for_that_assistant()
    {
        await using var harness = new ChatSessionHarness();
        var assistant = NewAssistant();

        await harness.Session.StartNewChatAsync(assistant);

        Assert.Equal(
            (true, false),
            (harness.Session.IsNewChatWith(assistant.Id), harness.Session.IsNewChatWith(assistantId: null)));
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
    public async Task The_estimated_context_counts_the_assistants_instructions()
    {
        await using var plainChat = new ChatSessionHarness();
        await using var assistantChat = new ChatSessionHarness();
        await assistantChat.Session.StartNewChatAsync(NewAssistant(instructions: new string('i', 3_000)));

        await plainChat.Session.SendAsync("hei");
        await assistantChat.Session.SendAsync("hei");

        Assert.True(assistantChat.Session.EstimatedContextTokens > plainChat.Session.EstimatedContextTokens);
    }

    private static Assistant NewAssistant(string instructions = AssistantInstructions) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Name = "Saksbehandler",
        Instructions = instructions,
        Visibility = AssistantVisibility.Private
    };

    private static ChatEntity StoreChatWith(ChatSessionHarness harness, Guid assistantId) =>
        harness.Repository.Store(new ChatEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ChatSessionHarness.OwnerUnderTest,
            AssistantId = assistantId,
            Title = "stored",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
}
