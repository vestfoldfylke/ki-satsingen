using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TurnBuilderTests
{
    [Fact]
    public void Text_that_streams_without_interruption_is_one_segment()
    {
        var builder = Builder();

        builder.Apply(Text("Hei "));
        builder.Apply(Text("der"));

        Assert.Equal("Hei der", Assert.IsType<TextSegment>(Assert.Single(builder.Snapshot().Answer)).Text);
    }

    [Fact]
    public void A_tool_call_ends_the_text_before_it_so_later_text_is_a_segment_of_its_own()
    {
        var builder = Builder();

        builder.Apply(Text("før"));
        builder.Apply(Call("call-1"));
        builder.Apply(Result("call-1"));
        builder.Apply(Text("etter"));

        Assert.Equal(["før", "etter"], builder.Snapshot().Answer.OfType<TextSegment>().Select(text => text.Text));
    }

    [Fact]
    public void A_tool_call_is_running_until_its_result_arrives()
    {
        var builder = Builder();

        builder.Apply(Call("call-1"));

        Assert.Equal(ToolStatus.Running, SingleTool(builder).Status);
    }

    [Fact]
    public void A_result_completes_the_call_it_answers()
    {
        var builder = Builder();

        builder.Apply(Call("call-1"));
        builder.Apply(Result("call-1"));

        Assert.Equal(ToolStatus.Completed, SingleTool(builder).Status);
    }

    // Replayed next turn, it must read exactly as it did the first time: unquoted.
    [Fact]
    public void A_text_result_is_kept_as_the_text_the_provider_was_sent()
    {
        var builder = Builder();

        builder.Apply(Call("call-1"));
        builder.Apply(Result("call-1", "tolv"));

        Assert.Equal("tolv", SingleTool(builder).Result);
    }

    // Byte for byte against the adapter in ToolResultReplayTests; here, that it is the JSON.
    [Fact]
    public void A_structured_result_is_kept_as_its_json()
    {
        var builder = Builder();

        builder.Apply(Call("call-1"));
        builder.Apply(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", new { hour = 12 })]));

        using var stored = System.Text.Json.JsonDocument.Parse(SingleTool(builder).Result!);
        Assert.Equal(12, stored.RootElement.GetProperty("hour").GetInt32());
    }

    // FunctionInvokingChatClient reports a throwing tool this way; see FunctionInvocationOrderingTests.
    [Fact]
    public void A_result_carrying_an_exception_marks_its_call_failed()
    {
        var builder = Builder();

        builder.Apply(Call("call-1"));
        builder.Apply(new ChatResponseUpdate(ChatRole.Tool,
            [new FunctionResultContent("call-1", "Error: Function failed.") { Exception = new InvalidOperationException("broke") }]));

        Assert.Equal(ToolStatus.Failed, SingleTool(builder).Status);
    }

    [Fact]
    public void A_call_keeps_the_arguments_the_model_gave_it()
    {
        var builder = Builder();

        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "search", new Dictionary<string, object?> { ["query"] = "vær" })]));

        Assert.Equal("vær", SingleTool(builder).Arguments.GetProperty("query").GetString());
    }

    [Fact]
    public void What_the_model_says_after_a_tool_result_belongs_to_the_next_round()
    {
        var builder = Builder();

        builder.Apply(Text("før"));
        builder.Apply(Call("call-1"));
        builder.Apply(Result("call-1"));
        builder.Apply(Text("etter"));

        Assert.Equal([0, 0, 1], builder.Snapshot().Answer.Select(segment => segment.Round));
    }

    // Otherwise the next round's text joins it and is replayed before the result.
    [Fact]
    public void Text_written_after_a_call_stays_in_that_calls_round()
    {
        var builder = Builder();

        builder.Apply(Text("a"));
        builder.Apply(Call("call-1"));
        builder.Apply(Text("b"));
        builder.Apply(Result("call-1"));
        builder.Apply(Text("c"));

        var texts = builder.Snapshot().Answer.OfType<TextSegment>().Select(text => (text.Text, text.Round));
        Assert.Equal([("a", 0), ("b", 0), ("c", 1)], texts);
    }

    [Fact]
    public void Calls_made_together_share_their_round()
    {
        var builder = Builder();

        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "a"), new FunctionCallContent("call-2", "b")]));
        builder.Apply(new ChatResponseUpdate(ChatRole.Tool,
            [new FunctionResultContent("call-1", "x"), new FunctionResultContent("call-2", "y")]));

        Assert.All(builder.Snapshot().Answer, segment => Assert.Equal(0, segment.Round));
    }

    // The streamer relies on this order: the text is flushed and its stream ended
    // before the page renders the tool that follows it.
    [Fact]
    public void Text_and_a_call_in_one_update_change_the_answer_in_the_order_they_arrived()
    {
        var builder = Builder();

        var changes = builder.Apply(new ChatResponseUpdate(ChatRole.Assistant,
            [new TextContent("Sjekker"), new FunctionCallContent("call-1", "probe")]));

        Assert.Collection(
            changes,
            change => Assert.IsType<TurnChange.TextStarted>(change),
            change => Assert.IsType<TurnChange.TextAppended>(change),
            change => Assert.IsType<TurnChange.TextEnded>(change),
            change => Assert.IsType<TurnChange.ToolStarted>(change));
    }

    [Fact]
    public void Text_that_keeps_streaming_changes_nothing_the_page_renders()
    {
        var builder = Builder();
        builder.Apply(Text("Hei"));

        var changes = builder.Apply(Text(" der"));

        Assert.IsType<TurnChange.TextAppended>(Assert.Single(changes));
    }

    [Fact]
    public void Content_the_transcript_does_not_keep_leaves_the_answer_empty()
    {
        var builder = Builder();

        builder.Apply(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("tenker")]));

        Assert.Empty(builder.Snapshot().Answer);
    }

    [Fact]
    public void A_tool_still_running_when_its_turn_is_stopped_is_interrupted()
    {
        var builder = Builder();
        builder.Apply(Call("call-1"));

        var stopped = builder.Finish(TurnStatus.Stopped);

        Assert.Equal(ToolStatus.Interrupted, Assert.IsType<ToolSegment>(Assert.Single(stopped.Answer)).Status);
    }

    [Fact]
    public void Usage_is_summed_across_the_round_trips_of_a_turn()
    {
        var builder = Builder();
        builder.DurationMs = 10;

        builder.Apply(Usage(100, 10));
        builder.Apply(Usage(200, 20));

        Assert.Equal(new MessageUsage(300, 30), builder.Finish(TurnStatus.Completed).Metadata?.Usage);
    }

    [Fact]
    public void A_stopped_turn_the_provider_never_reported_on_estimates_its_request_and_what_was_written()
    {
        var builder = Builder(requestTokens: 50);
        builder.DurationMs = 10;
        builder.Apply(Text("halvferdig"));

        var usage = builder.Finish(TurnStatus.Stopped).Metadata?.Usage;

        var written = ContextTokenEstimator.EstimateGenerated(Assert.Single(builder.Snapshot().Answer));
        Assert.Equal(new MessageUsage(null, null, 50, written), usage);
    }

    // The usage chunk arrives before the tool result, so it is the calling round's.
    [Fact]
    public void A_stopped_tool_turn_keeps_the_reported_round_and_estimates_only_the_one_in_flight()
    {
        var builder = Builder(requestTokens: 50);
        builder.DurationMs = 10;
        builder.Apply(Call("call-1"));
        builder.Apply(Usage(500, 20));
        builder.Apply(Result("call-1"));
        builder.Apply(Text("halvferdig"));

        var usage = builder.Finish(TurnStatus.Stopped).Metadata?.Usage;

        var answer = builder.Snapshot().Answer;
        var resentForRoundTwo = 50 + ContextTokenEstimator.EstimateResent(answer[0]);
        Assert.Equal(new MessageUsage(500, 20, resentForRoundTwo, ContextTokenEstimator.EstimateGenerated(answer[1])), usage);
    }

    // A request the provider refused before answering is not billed.
    [Fact]
    public void A_stopped_turn_that_received_nothing_estimates_nothing()
    {
        var builder = Builder(requestTokens: 50);
        builder.DurationMs = 10;

        Assert.Null(builder.Finish(TurnStatus.Stopped).Metadata?.Usage);
    }

    [Fact]
    public void A_turn_the_model_was_never_asked_has_no_metadata()
    {
        var builder = Builder();

        Assert.Null(builder.Finish(TurnStatus.Failed, TurnStage.SavingMessage).Metadata);
    }

    [Fact]
    public void A_failed_turn_records_the_stage_it_failed_at()
    {
        var builder = Builder();

        Assert.Equal(TurnStage.Generating, builder.Finish(TurnStatus.Failed, TurnStage.Generating).FailedAt);
    }

    private static TurnBuilder Builder(long requestTokens = 0) => new(new Turn
    {
        Id = Guid.NewGuid(),
        Prompt = "hei",
        SystemPrompt = "be brief",
        ModelKey = FakeChatModelCatalog.DefaultKey,
        ModelDisplayName = "Fast",
        StartedAt = DateTimeOffset.UtcNow
    }, requestTokens);

    private static ToolSegment SingleTool(TurnBuilder builder) =>
        Assert.IsType<ToolSegment>(Assert.Single(builder.Snapshot().Answer));

    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static ChatResponseUpdate Call(string callId) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, "probe")]);

    private static ChatResponseUpdate Result(string callId, string result = "ok") =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, result)]);

    private static ChatResponseUpdate Usage(long input, long output) =>
        new(ChatRole.Assistant,
        [
            new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output })
        ]);
}
