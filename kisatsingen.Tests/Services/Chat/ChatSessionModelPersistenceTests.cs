using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
// This namespace ends in .Chat, which shadows the entity of the same name.
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

    // The key answers "which catalogue entry was chosen", which is a question about
    // the assistant's turn. A user message has no model.
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
        harness.Repository.StoredChat = ChatAnsweredBy(FakeChatModelCatalog.AlternativeKey.Value);

        await harness.Session.LoadAsync(harness.Repository.StoredChat.Id);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    // A model can be dropped from the catalogue between one session and the next.
    // The chat is still readable and still worth continuing, so the stored key is
    // treated as a preference that can expire rather than a requirement.
    [Fact]
    public async Task A_chat_whose_model_has_left_the_catalogue_falls_back_to_the_default()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.StoredChat = ChatAnsweredBy("a-model-that-was-removed");

        await harness.Session.LoadAsync(harness.Repository.StoredChat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    // Rows written before the picker existed carry no key at all.
    [Fact]
    public async Task A_chat_with_no_recorded_model_falls_back_to_the_default()
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.StoredChat = ChatAnsweredBy(null);

        await harness.Session.LoadAsync(harness.Repository.StoredChat.Id);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    // The column is nullable varchar with no non-empty constraint, so a blank is
    // reachable however it got there. Reading it used to throw out of the
    // ChatModelKey constructor and take LoadAsync with it, which cost the user the
    // whole chat over one unreadable field on one row.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_chat_whose_recorded_model_is_blank_still_opens(string modelKey)
    {
        await using var harness = new ChatSessionHarness();
        harness.Repository.StoredChat = ChatAnsweredBy(modelKey);

        await harness.Session.LoadAsync(harness.Repository.StoredChat.Id);

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

        // Asserted first: PendingModel is also null when nothing has ever
        // answered, so without this the null below could mean the switch never
        // registered rather than that the turn took it into effect.
        Assert.NotNull(harness.Session.PendingModel);

        await harness.Session.SendAsync("hei igjen");

        Assert.Null(harness.Session.PendingModel);
    }

    // There is no previous model to contrast with, so nothing is pending — the
    // picker's own label already says what will answer.
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
