using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionModelPersistenceTests
{
    [Fact]
    public async Task A_turn_records_the_model_key_it_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await harness.Session.SendAsync("hei");

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.StoredTurn.ModelKey);
    }

    [Fact]
    public async Task Reopening_a_chat_resumes_the_model_it_was_last_used_with()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatLastUsedWith(FakeChatModelCatalog.AlternativeKey.Value));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    // The stored key is a preference that can expire; the chat is still worth continuing.
    [Fact]
    public async Task A_chat_whose_model_has_left_the_catalogue_falls_back_to_the_default()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatLastUsedWith("a-model-that-was-removed"));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    // One bad field must not make the chat unopenable.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_chat_whose_recorded_model_is_blank_still_opens(string modelKey)
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatLastUsedWith(modelKey));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task A_model_that_has_left_the_catalogue_is_named_by_its_key()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatLastUsedWith("a-model-that-was-removed"));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal("a-model-that-was-removed", harness.VisibleTurn.ModelDisplayName);
    }

    private static ChatEntity ChatLastUsedWith(string modelKey) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Title = "stored",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Turns =
        [
            new ChatTurn
            {
                Id = Guid.NewGuid(),
                Prompt = "hei",
                SystemPrompt = "be brief",
                ModelKey = modelKey,
                Status = nameof(TurnStatus.Completed),
                AnswerJson = "[]"
            }
        ]
    };
}
