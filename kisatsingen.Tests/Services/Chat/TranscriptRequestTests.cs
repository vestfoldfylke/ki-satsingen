using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TranscriptRequestTests
{
    // The system prompt travels as ChatOptions.Instructions now. Building one here
    // as well would send it twice — once as an instruction, once as a message the
    // model reads as part of the conversation.
    [Fact]
    public void The_system_prompt_is_not_built_into_the_message_list()
    {
        var entries = new List<TranscriptEntry> { User("hei") };

        var request = TranscriptRequest.Build(entries);

        Assert.DoesNotContain(request, message => message.Role == ChatRole.System);
    }

    // The reason events live in their own type at all. A leak here would send the
    // model a turn it never produced.
    [Fact]
    public void Events_are_never_sent_to_the_model()
    {
        var entries = new List<TranscriptEntry>
        {
            User("hei"),
            Stopped(),
            User("hei igjen")
        };

        var request = TranscriptRequest.Build(entries);

        Assert.Equal(["hei", "hei igjen"], request.Select(m => m.Text));
    }

    [Fact]
    public void Messages_keep_their_transcript_order()
    {
        var entries = new List<TranscriptEntry>
        {
            User("first"),
            Assistant("second"),
            User("third")
        };

        var request = TranscriptRequest.Build(entries);

        Assert.Equal(["first", "second", "third"], request.Select(m => m.Text));
    }

    [Fact]
    public void A_transcript_holding_only_events_produces_no_messages()
    {
        var entries = new List<TranscriptEntry> { Stopped() };

        var request = TranscriptRequest.Build(entries);

        Assert.Empty(request);
    }

    private static MessageEntry User(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.User, text), null);

    private static MessageEntry Assistant(string text) =>
        new(Guid.NewGuid(), new ChatMessage(ChatRole.Assistant, text), null);

    private static EventEntry Stopped() =>
        new(Guid.NewGuid(), ChatEventKind.Stopped, null, DateTimeOffset.UtcNow);
}
