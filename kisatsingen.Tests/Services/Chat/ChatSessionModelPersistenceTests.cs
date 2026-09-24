using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
// Aliased: this namespace's .Chat shadows the entity.
using ChatEntity = kisatsingen.Data.Entities.Chat;
using StoredMessage = kisatsingen.Data.Entities.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionModelPersistenceTests
{
    [Fact]
    public async Task The_assistant_message_records_the_model_key_the_turn_ran_on()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await harness.Session.SendAsync("hei");

        var assistant = harness.Repository.AppendedMessages
            .Single(message => message.Role == ChatRole.Assistant.Value);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey.Value, assistant.ModelKey);
    }

    [Fact]
    public async Task The_user_message_records_no_model_key()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        var user = harness.Repository.AppendedMessages
            .Single(message => message.Role == ChatRole.User.Value);

        Assert.Null(user.ModelKey);
    }

    [Fact]
    public async Task Reopening_a_chat_resumes_the_model_it_was_last_answered_by()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatAnsweredBy(FakeChatModelCatalog.AlternativeKey.Value));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    // The stored key is a preference that can expire; the chat is still worth continuing.
    [Fact]
    public async Task A_chat_whose_model_has_left_the_catalogue_falls_back_to_the_default()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatAnsweredBy("a-model-that-was-removed"));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    // Rows written before the picker existed.
    [Fact]
    public async Task A_chat_with_no_recorded_model_falls_back_to_the_default()
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatAnsweredBy(null));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    // The column allows blanks, and one bad field must not make the chat unopenable.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_chat_whose_recorded_model_is_blank_still_opens(string modelKey)
    {
        await using var harness = new ChatSessionHarness();
        var chat = harness.Repository.Store(ChatAnsweredBy(modelKey));

        await harness.Session.LoadAsync(chat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task A_switch_that_has_not_taken_effect_yet_is_reported_as_pending()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("hei");

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.PendingModel?.Key);
    }

    [Fact]
    public async Task A_switch_stops_being_pending_once_a_turn_has_run_on_it()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("hei");
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        // PendingModel is also null when nothing has answered; rule that out first.
        Assert.NotNull(harness.Session.PendingModel);

        await harness.Session.SendAsync("hei igjen");

        Assert.Null(harness.Session.PendingModel);
    }

    [Fact]
    public async Task Nothing_is_pending_before_the_first_answer()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        Assert.Null(harness.Session.PendingModel);
    }

    private static ChatEntity ChatAnsweredBy(string? modelKey) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ChatSessionHarness.OwnerUnderTest,
        Title = "stored",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Messages =
        [
            new StoredMessage { Role = ChatRole.User.Value, Content = "hei" },
            new StoredMessage { Role = ChatRole.Assistant.Value, Content = "hei selv", ModelKey = modelKey }
        ]
    };
}
