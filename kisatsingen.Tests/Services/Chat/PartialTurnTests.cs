using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

public sealed class PartialTurnTests
{
    // Providers reject a call with no result, which would block the chat for good.
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

    // Valid history: the answer depends on the result.
    [Fact]
    public void A_tool_exchange_followed_by_an_answer_is_kept()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, [Call("answered")]),
            new AiMessage(ChatRole.Tool, [new FunctionResultContent("answered", "sunny")]),
            new AiMessage(ChatRole.Assistant, "Det er sol"));

        var pruned = PartialTurn.Prune(response);

        Assert.Equal([ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant], pruned!.Messages.Select(message => message.Role));
    }

    // Kept, the next user message would follow a tool message, which Mistral rejects.
    [Fact]
    public void A_tool_exchange_the_model_never_answered_from_is_dropped()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, "Jeg sjekker"),
            new AiMessage(ChatRole.Assistant, [Call("answered")]),
            new AiMessage(ChatRole.Tool, [new FunctionResultContent("answered", "sunny")]));

        var pruned = PartialTurn.Prune(response);

        Assert.Equal(ChatRole.Assistant, pruned!.Messages[^1].Role);
    }

    [Fact]
    public void A_turn_that_produced_only_a_tool_exchange_leaves_nothing_to_store()
    {
        var response = Response(
            new AiMessage(ChatRole.Assistant, [Call("answered")]),
            new AiMessage(ChatRole.Tool, [new FunctionResultContent("answered", "sunny")]));

        Assert.Null(PartialTurn.Prune(response));
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

    [Fact]
    public void A_blank_message_leaves_nothing_to_store()
    {
        var response = Response(new AiMessage(ChatRole.Assistant, [new TextContent("   ")]));

        Assert.Null(PartialTurn.Prune(response));
    }

    private static ChatResponse Response(params AiMessage[] messages) => new([.. messages]);

    private static FunctionCallContent Call(string callId) =>
        new(callId, "get_weather", new Dictionary<string, object?>());
}
