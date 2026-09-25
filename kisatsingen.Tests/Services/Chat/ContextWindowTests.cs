using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// Behaviour, not figures: the estimate's constants are tuning, and pinning them
// would make every adjustment a test edit that proves nothing.
public sealed class ContextWindowTests
{
    [Fact]
    public async Task A_conversation_with_nothing_to_send_has_no_size()
    {
        await using var harness = new ChatSessionHarness();

        Assert.Null(harness.Session.EstimatedContextTokens);
    }

    [Fact]
    public async Task The_size_grows_as_the_conversation_does()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SendAsync("hei");
        var afterOneTurn = harness.Session.EstimatedContextTokens;

        await harness.Session.SendAsync(new string('a', 3_000));

        Assert.True(harness.Session.EstimatedContextTokens > afterOneTurn);
    }

    // Reported usage double-counts tool round trips and is missing from some
    // providers; measuring the request avoids both.
    [Fact]
    public async Task The_size_does_not_depend_on_what_the_provider_reported()
    {
        await using var measured = new ChatSessionHarness();
        measured.Client.OnStream = _ => ModelStream.AnsweringWithUsage("hei", inputTokens: 900, outputTokens: 100);

        await using var unmeasured = new ChatSessionHarness();
        unmeasured.Client.OnStream = _ => ModelStream.Answering("hei");

        await measured.Session.SendAsync("hei");
        await unmeasured.Session.SendAsync("hei");

        // Two nulls would also pass the comparison below.
        Assert.NotNull(unmeasured.Session.EstimatedContextTokens);
        Assert.Equal(unmeasured.Session.EstimatedContextTokens, measured.Session.EstimatedContextTokens);
    }

    [Fact]
    public void A_conversation_larger_than_the_window_would_overflow_it()
    {
        Assert.True(ModelHolding(1_000).WouldOverflow(1_001));
    }

    [Fact]
    public void A_conversation_that_exactly_fills_the_window_does_not_overflow_it()
    {
        Assert.False(ModelHolding(1_000).WouldOverflow(1_000));
    }

    [Fact]
    public void An_unmeasured_conversation_never_overflows()
    {
        Assert.False(ModelHolding(1).WouldOverflow(null));
    }

    [Fact]
    public void An_unmeasured_conversation_is_roomy()
    {
        Assert.Equal(ContextFillLevel.Roomy, ModelHolding(1).ClassifyContext(null));
    }

    [Fact]
    public void A_conversation_just_below_the_filling_threshold_is_roomy()
    {
        var model = ModelHolding(1_000);

        Assert.Equal(ContextFillLevel.Roomy, model.ClassifyContext(model.FillingThresholdTokens - 1));
    }

    [Fact]
    public void A_conversation_at_the_filling_threshold_is_filling()
    {
        var model = ModelHolding(1_000);

        Assert.Equal(ContextFillLevel.Filling, model.ClassifyContext(model.FillingThresholdTokens));
    }

    [Fact]
    public void A_conversation_at_the_nearly_full_threshold_is_nearly_full()
    {
        var model = ModelHolding(1_000);

        Assert.Equal(ContextFillLevel.NearlyFull, model.ClassifyContext(model.NearlyFullThresholdTokens));
    }

    // Must agree with WouldOverflow, which the model picker also uses.
    [Fact]
    public void A_conversation_that_exactly_fills_the_window_is_nearly_full_not_overflowing()
    {
        Assert.Equal(ContextFillLevel.NearlyFull, ModelHolding(1_000).ClassifyContext(1_000));
    }

    [Fact]
    public void A_conversation_larger_than_the_window_is_overflowing()
    {
        Assert.Equal(ContextFillLevel.Overflowing, ModelHolding(1_000).ClassifyContext(1_001));
    }

    [Fact]
    public void The_filling_threshold_comes_before_the_nearly_full_threshold()
    {
        var model = ModelHolding(1_000);

        Assert.True(model.FillingThresholdTokens < model.NearlyFullThresholdTokens);
    }

    private static ChatModel ModelHolding(int contextWindowTokens) => new()
    {
        Key = new ChatModelKey("under-test"),
        DisplayName = "Under test",
        ShortDescription = "short",
        LongDescription = "long",
        Provider = "test",
        ModelId = "wire-id",
        ContextWindowTokens = contextWindowTokens,
        IconName = "icon"
    };
}

public sealed class ContextTokenEstimatorTests
{
    [Fact]
    public void An_empty_request_with_no_system_prompt_costs_nothing()
    {
        Assert.Equal(0, ContextTokenEstimator.Estimate([], systemPrompt: null));
    }

    [Fact]
    public void A_longer_conversation_estimates_larger_than_a_shorter_one()
    {
        var shorter = ContextTokenEstimator.Estimate([User("hei")], systemPrompt: null);
        var longer = ContextTokenEstimator.Estimate([User(new string('a', 1_000))], systemPrompt: null);

        Assert.True(longer > shorter);
    }

    // Easy to miss: it travels as Instructions, not in the message list.
    [Fact]
    public void The_system_prompt_counts_toward_the_estimate()
    {
        var withoutPrompt = ContextTokenEstimator.Estimate([User("hei")], systemPrompt: null);
        var withPrompt = ContextTokenEstimator.Estimate([User("hei")], new string('s', 900));

        Assert.True(withPrompt > withoutPrompt);
    }

    // Often the largest thing in a conversation.
    [Fact]
    public void A_tool_result_counts_toward_the_estimate()
    {
        var withoutResult = ContextTokenEstimator.Estimate([User("hei")], systemPrompt: null);

        var withResult = ContextTokenEstimator.Estimate(
            [User("hei"), ToolResult(new string('r', 3_000))],
            systemPrompt: null);

        Assert.True(withResult > withoutResult + 900);
    }

    [Fact]
    public void A_tool_calls_arguments_count_toward_the_estimate()
    {
        var withoutCall = ContextTokenEstimator.Estimate([User("hei")], systemPrompt: null);

        var withCall = ContextTokenEstimator.Estimate(
            [User("hei"), ToolCall("search", new string('q', 3_000))],
            systemPrompt: null);

        Assert.True(withCall > withoutCall + 900);
    }

    private static AiMessage User(string text) => new(ChatRole.User, text);

    private static AiMessage ToolResult(string result) =>
        new(ChatRole.Tool, [new FunctionResultContent("call-1", result)]);

    private static AiMessage ToolCall(string name, string argument) =>
        new(ChatRole.Assistant,
        [
            new FunctionCallContent("call-1", name, new Dictionary<string, object?> { ["query"] = argument })
        ]);
}
