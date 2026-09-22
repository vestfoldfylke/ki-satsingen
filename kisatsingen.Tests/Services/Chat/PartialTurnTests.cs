using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// What survives from a turn that stopped mid-flight, and — more importantly —
// what must not.
public sealed class PartialTurnTests
{
    // The rule the whole file exists for. A stored tool call that nothing answered
    // goes back out with every later request in the chat, and providers reject
    // that, so keeping it would not cost a half-answer — it would leave the
    // conversation permanently unable to send.
    [Fact]
    public void A_tool_call_that_was_never_answered_is_dropped()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, [Call("unanswered"), new TextContent("jeg sjekker")]));

        var pruned = PartialTurn.Prune(response);

        Assert.DoesNotContain(
            pruned!.Messages.SelectMany(message => message.Contents),
            content => content is FunctionCallContent);
    }

    [Fact]
    public void The_text_alongside_an_unanswered_tool_call_is_kept()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, [Call("unanswered"), new TextContent("jeg sjekker")]));

        var pruned = PartialTurn.Prune(response);

        Assert.Equal("jeg sjekker", pruned!.Messages.Single().Text);
    }

    // The completed half of a tool loop is a matched pair and stays: it is valid
    // history, and the model needs the result it already acted on.
    [Fact]
    public void A_tool_call_that_was_answered_is_kept()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, [Call("answered")]),
            new AiMessage(ChatRole.Tool, [new FunctionResultContent("answered", "sunny")]));

        var pruned = PartialTurn.Prune(response);

        Assert.Contains(
            pruned!.Messages.SelectMany(message => message.Contents),
            content => content is FunctionCallContent);
    }

    [Fact]
    public void A_turn_that_produced_only_an_unanswered_tool_call_leaves_nothing_to_store()
    {
        var response = Response(new AiMessage(ChatRole.Assistant, [Call("unanswered")]));

        Assert.Null(PartialTurn.Prune(response));
    }

    [Fact]
    public void A_turn_that_produced_nothing_leaves_nothing_to_store()
    {
        Assert.Null(PartialTurn.Prune(Response()));
    }

    // An empty streaming chunk degrades to this, and storing it would put a silent
    // assistant bubble in the transcript.
    [Fact]
    public void A_blank_message_leaves_nothing_to_store()
    {
        var response = Response(new AiMessage(ChatRole.Assistant, [new TextContent("   ")]));

        Assert.Null(PartialTurn.Prune(response));
    }

    // Whatever the completed round trips reported is real, provider-measured usage
    // and has to survive the pruning — it is the only usage a stopped turn has.
    [Fact]
    public void The_usage_reported_before_the_stop_is_kept()
    {
        var response = Response(new AiMessage(ChatRole.Assistant, [new TextContent("halvferdig")]));
        response.Usage = new UsageDetails { InputTokenCount = 900, OutputTokenCount = 100 };

        var pruned = PartialTurn.Prune(response);

        Assert.Equal(900, pruned!.Usage?.InputTokenCount);
    }

    private static ChatResponse Response(params AiMessage[] messages) => new([.. messages]);

    private static FunctionCallContent Call(string callId) =>
        new(callId, "get_weather", new Dictionary<string, object?>());
}
