using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TranscriptRequestTests
{
    private const string SystemPrompt = "be helpful";

    [Fact]
    public void the_system_prompt_is_prepended_to_the_conversation()
    {
        var entries = new List<TranscriptEntry> { User("hei") };

        var request = TranscriptRequest.Build(entries, SystemPrompt);

        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal(SystemPrompt, request[0].Text);
    }

    // The reason events live in their own type at all. A leak here would send the
    // model a turn it never produced.
    [Fact]
    public void events_are_never_sent_to_the_model()
    {
        var entries = new List<TranscriptEntry>
        {
            User("hei"),
            Stopped(),
            User("hei igjen")
        };

        var request = TranscriptRequest.Build(entries, SystemPrompt);

        Assert.Equal(["be helpful", "hei", "hei igjen"], request.Select(m => m.Text));
    }

    [Fact]
    public void messages_keep_their_transcript_order()
    {
        var entries = new List<TranscriptEntry>
        {
            User("first"),
            Assistant("second"),
            User("third")
        };

        var request = TranscriptRequest.Build(entries, SystemPrompt);

        Assert.Equal(["be helpful", "first", "second", "third"], request.Select(m => m.Text));
    }

    [Fact]
    public void a_transcript_holding_only_events_produces_just_the_system_prompt()
    {
        var entries = new List<TranscriptEntry> { Stopped() };

        var request = TranscriptRequest.Build(entries, SystemPrompt);

        Assert.Equal(ChatRole.System, Assert.Single(request).Role);
    }

    private static MessageEntry User(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.User, text), null);

    private static MessageEntry Assistant(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.Assistant, text), null);

    private static EventEntry Stopped() =>
        new(Guid.NewGuid(), ChatEventKind.Stopped, null, DateTimeOffset.UtcNow);
}
