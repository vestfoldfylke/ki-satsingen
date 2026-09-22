using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// The model boundary is derived from the ModelKey each turn records, never stored.
// These tests pin which question carries it, and when it is not drawn at all.
public sealed class TranscriptProjectionModelBoundaryTests
{
    private static readonly ChatModelKey Fast = new("fast");
    private static readonly ChatModelKey Large = new("large");

    // It marks the question whose *answer* came from the new model, not the last
    // question the old model answered.
    [Fact]
    public void The_question_answered_by_a_new_model_carries_the_boundary()
    {
        var views = TranscriptProjection.Build(
        [
            User("først"), AnsweredBy(Fast, "Rask"),
            User("så"), AnsweredBy(Large, "Kompleks")
        ]);

        var questions = views.OfType<UserBubbleView>().ToList();

        Assert.Null(questions[0].ModelChangedTo);
        Assert.Equal("Kompleks", questions[1].ModelChangedTo);
    }

    [Fact]
    public void Consecutive_turns_on_one_model_carry_no_boundary()
    {
        var views = TranscriptProjection.Build(
        [
            User("først"), AnsweredBy(Fast, "Rask"),
            User("så"), AnsweredBy(Fast, "Rask")
        ]);

        Assert.All(views.OfType<UserBubbleView>(), question => Assert.Null(question.ModelChangedTo));
    }

    [Fact]
    public void The_first_question_in_a_chat_carries_no_boundary()
    {
        var views = TranscriptProjection.Build([User("hei"), AnsweredBy(Fast, "Rask")]);

        Assert.Null(Assert.Single(views.OfType<UserBubbleView>()).ModelChangedTo);
    }

    // Rows written before the picker existed carry no key. A null on either side
    // means "unknown", which is not the same as "changed".
    [Fact]
    public void Turns_with_no_recorded_model_carry_no_boundary()
    {
        var views = TranscriptProjection.Build(
        [
            User("først"), AnsweredBy(null, null),
            User("så"), AnsweredBy(null, null)
        ]);

        Assert.All(views.OfType<UserBubbleView>(), question => Assert.Null(question.ModelChangedTo));
    }

    [Fact]
    public void Switching_back_marks_a_boundary_each_way()
    {
        var views = TranscriptProjection.Build(
        [
            User("en"), AnsweredBy(Fast, "Rask"),
            User("to"), AnsweredBy(Large, "Kompleks"),
            User("tre"), AnsweredBy(Fast, "Rask")
        ]);

        Assert.Equal(
            [null, "Kompleks", "Rask"],
            views.OfType<UserBubbleView>().Select(question => question.ModelChangedTo));
    }

    // A model dropped from the catalogue leaves the key as its own name, rather
    // than an empty divider.
    [Fact]
    public void A_model_with_no_display_name_is_named_by_its_key()
    {
        var views = TranscriptProjection.Build(
        [
            User("først"), AnsweredBy(Fast, "Rask"),
            User("så"), AnsweredBy(Large, null)
        ]);

        Assert.Equal("large", views.OfType<UserBubbleView>().Last().ModelChangedTo);
    }

    // Whatever else landed between the question and its answer, the boundary
    // belongs on the question — never on the notice that happens to sit nearest
    // the turn.
    [Fact]
    public void A_notice_between_the_question_and_its_answer_does_not_take_the_boundary()
    {
        var views = TranscriptProjection.Build(
        [
            User("først"), AnsweredBy(Fast, "Rask"),
            User("så"), Stopped(), AnsweredBy(Large, "Kompleks")
        ]);

        Assert.Equal("Kompleks", views.OfType<UserBubbleView>().Last().ModelChangedTo);
    }

    private static EventEntry Stopped() =>
        new(Guid.NewGuid(), kisatsingen.Data.Entities.ChatEventKind.Stopped, null, DateTimeOffset.UtcNow);

    private static MessageEntry User(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.User, text), null);

    private static MessageEntry AnsweredBy(ChatModelKey? key, string? displayName) =>
        new(
            Guid.NewGuid(),
            new ChatMessage(ChatRole.Assistant, "svar"),
            new TurnMetadata("provider-id", key, displayName, null, null, null, null, null, DateTimeOffset.UtcNow, null));
}
