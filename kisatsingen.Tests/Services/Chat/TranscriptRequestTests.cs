using System.Text.Json;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// What goes back to the provider. The rule under test throughout: a tool call is
// sent only if it has a result and the model said something after it.
public sealed class TranscriptRequestTests
{
    [Fact]
    public void Each_turn_sends_its_question_and_then_its_answer()
    {
        var request = TranscriptRequest.Build([TurnWith("hei", Text(0, "hei selv"))]);

        Assert.Equal(
            [(ChatRole.User, "hei"), (ChatRole.Assistant, "hei selv")],
            request.Select(message => (message.Role, message.Text)));
    }

    [Fact]
    public void A_tool_the_model_answered_after_is_sent_as_a_call_then_its_result()
    {
        var request = TranscriptRequest.Build([TurnWith("hva er klokka?",
            Text(0, "Sjekker."),
            Tool(0, "call-1", ToolStatus.Completed, "12:00"),
            Text(1, "Den er tolv."))]);

        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant],
            request.Select(message => message.Role));
        Assert.Equal("call-1", Assert.Single(request[1].Contents.OfType<FunctionCallContent>()).CallId);
        Assert.Equal("call-1", Assert.Single(request[2].Contents.OfType<FunctionResultContent>()).CallId);
    }

    // Every provider rejects a call with no result.
    [Fact]
    public void An_interrupted_call_is_not_sent()
    {
        var request = TranscriptRequest.Build([TurnWith("hei",
            Text(0, "Sjekker."),
            Tool(0, "call-1", ToolStatus.Interrupted, result: null))]);

        Assert.DoesNotContain(request, message => message.Contents.OfType<FunctionCallContent>().Any());
    }

    // Mistral rejects a question that follows a tool message directly.
    [Fact]
    public void A_result_the_model_never_answered_after_is_not_sent()
    {
        var request = TranscriptRequest.Build([TurnWith("hei",
            Text(0, "Sjekker."),
            Tool(0, "call-1", ToolStatus.Completed, "12:00"))]);

        Assert.DoesNotContain(request, message => message.Role == ChatRole.Tool);
    }

    [Fact]
    public void A_turn_with_no_answer_sends_only_its_question()
    {
        var request = TranscriptRequest.Build([TurnWith("hei")]);

        Assert.Equal(ChatRole.User, Assert.Single(request).Role);
    }

    // A reload would not have it, so the model must not have seen it either.
    [Theory]
    [InlineData(TurnStage.Authenticating)]
    [InlineData(TurnStage.SavingMessage)]
    public void A_question_that_was_never_saved_is_not_sent(TurnStage failedAt)
    {
        var unsaved = TurnWith("aldri lagret") with { Status = TurnStatus.Failed, FailedAt = failedAt };

        var request = TranscriptRequest.Build([unsaved, TurnWith("prøv igjen")]);

        Assert.Equal(["prøv igjen"], request.Select(message => message.Text));
    }

    // "prøv igjen" means nothing without the question it refers to.
    [Fact]
    public void A_saved_question_that_failed_before_any_answer_is_still_sent()
    {
        var unanswered = TurnWith("hva er budsjettet?") with { Status = TurnStatus.Failed, FailedAt = TurnStage.Generating };

        var request = TranscriptRequest.Build([unanswered, TurnWith("prøv igjen")]);

        Assert.Equal(["hva er budsjettet?", "prøv igjen"], request.Select(message => message.Text));
    }

    [Fact]
    public void A_failed_tool_is_sent_with_the_result_the_model_saw()
    {
        var request = TranscriptRequest.Build([TurnWith("hei",
            Tool(0, "call-1", ToolStatus.Failed, "Error: Function failed."),
            Text(1, "Det gikk ikke."))]);

        var result = Assert.Single(request.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("Error: Function failed.", result.Result);
    }

    [Fact]
    public void A_call_is_sent_with_the_arguments_the_model_gave_it()
    {
        var arguments = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["query"] = "vær" });
        var tool = Tool(0, "call-1", ToolStatus.Completed, "sol") with { Arguments = arguments };

        var request = TranscriptRequest.Build([TurnWith("hei", tool, Text(1, "Sol."))]);

        var call = Assert.Single(request.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("vær", ((JsonElement)call.Arguments!["query"]!).GetString());
    }

    private static Turn TurnWith(string prompt, params TurnSegment[] answer) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = prompt,
        SystemPrompt = "be brief",
        ModelKey = FakeChatModelCatalog.DefaultKey,
        ModelDisplayName = "Fast",
        StartedAt = DateTimeOffset.UtcNow,
        Answer = answer,
        Status = TurnStatus.Completed
    };

    private static TextSegment Text(int round, string text) => new(Guid.NewGuid(), round, text);

    private static ToolSegment Tool(int round, string callId, ToolStatus status, string? result) => new(
        Guid.NewGuid(),
        round,
        callId,
        "probe",
        JsonSerializer.SerializeToElement(new Dictionary<string, string>()),
        status,
        result);
}
