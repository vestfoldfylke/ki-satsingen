using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TranscriptProjectionTests
{
    [Fact]
    public void A_user_message_becomes_its_own_bubble()
    {
        var views = TranscriptProjection.Build([User("hei")]);

        var bubble = Assert.IsType<UserBubbleView>(Assert.Single(views));
        Assert.Equal("hei", bubble.Text);
    }

    [Fact]
    public void Assistant_and_tool_messages_between_two_user_messages_collapse_into_one_turn()
    {
        var views = TranscriptProjection.Build([
            User("hei"),
            Assistant("calling a tool"),
            Tool("tool result"),
            Assistant("here is the answer"),
            User("takk")
        ]);

        Assert.Collection(views,
            v => Assert.IsType<UserBubbleView>(v),
            v => Assert.Equal(3, Assert.IsType<AssistantTurnView>(v).Parts.Count),
            v => Assert.IsType<UserBubbleView>(v));
    }

    // The shape a stop produces: the user's message stands, and the event
    // explains why nothing followed it.
    [Fact]
    public void A_stopped_turn_renders_the_event_with_no_assistant_turn()
    {
        var views = TranscriptProjection.Build([User("hei"), Stopped()]);

        Assert.Collection(views,
            v => Assert.IsType<UserBubbleView>(v),
            v => Assert.Equal(ChatEventKind.Stopped, Assert.IsType<ChatEventView>(v).Kind));
        Assert.DoesNotContain(views, v => v is AssistantTurnView);
    }

    [Fact]
    public void An_event_closes_the_turn_it_belongs_to_so_it_renders_after_partial_output()
    {
        var views = TranscriptProjection.Build([
            User("hei"),
            Assistant("partial"),
            Stopped()
        ]);

        Assert.Collection(views,
            v => Assert.IsType<UserBubbleView>(v),
            v => Assert.IsType<AssistantTurnView>(v),
            v => Assert.IsType<ChatEventView>(v));
    }

    [Fact]
    public void A_turn_reports_the_last_assistant_metadata_and_the_first_start_time()
    {
        var firstAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var lastAt = new DateTimeOffset(2026, 3, 1, 12, 0, 9, TimeSpan.Zero);

        var views = TranscriptProjection.Build([
            User("hei"),
            Assistant("first", Metadata("gpt-first", firstAt)),
            Assistant("last", Metadata("gpt-last", lastAt))
        ]);

        var turn = Assert.IsType<AssistantTurnView>(views[1]);
        Assert.Equal("gpt-last", turn.Metadata!.ModelId);
        Assert.Equal(firstAt, turn.StartedAt);
    }

    [Fact]
    public void View_ids_are_carried_through_so_render_keys_stay_stable()
    {
        var user = User("hei");

        var views = TranscriptProjection.Build([user]);

        Assert.Equal(user.ViewId, Assert.Single(views).Id);
    }

    [Fact]
    public void An_empty_transcript_produces_no_views()
    {
        Assert.Empty(TranscriptProjection.Build([]));
    }

    private static MessageEntry User(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.User, text), null);

    private static MessageEntry Assistant(string text, AssistantMetadata? metadata = null) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.Assistant, text), metadata);

    private static MessageEntry Tool(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.Tool, text), null);

    private static EventEntry Stopped() =>
        new(Guid.NewGuid(), ChatEventKind.Stopped, null, DateTimeOffset.UtcNow);

    private static AssistantMetadata Metadata(string modelId, DateTimeOffset createdAt) =>
        new(modelId, null, null, null, null, null, createdAt, null);
}
