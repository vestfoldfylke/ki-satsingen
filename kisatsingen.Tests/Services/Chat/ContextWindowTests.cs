using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;
using Xunit;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Tests.Services.Chat;

// The size of a conversation, and whether a model can still hold it.
//
// These assert on behaviour rather than on exact figures. The estimate's
// constants are tuning, and a test that pins them turns every adjustment into a
// test edit while proving nothing about whether the number is useful.
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

    // The bug this rewrite exists for. A turn that calls a tool makes one provider
    // call per round trip, and ChatResponse.Usage sums them — so the old
    // usage-derived size roughly doubled on any turn that used a tool. Measuring
    // the request instead cannot double-count, because there is only one request.
    //
    // This also covers a provider that reports no usage at all, which used to
    // leave the size null so the warning could never fire.
    [Fact]
    public async Task The_size_does_not_depend_on_what_the_provider_reported()
    {
        await using var measured = new ChatSessionHarness();
        measured.Client.OnStream = _ => ModelStream.AnsweringWithUsage("hei", inputTokens: 900, outputTokens: 100);

        await using var unmeasured = new ChatSessionHarness();
        unmeasured.Client.OnStream = _ => ModelStream.Answering("hei");

        await measured.Session.SendAsync("hei");
        await unmeasured.Session.SendAsync("hei");

        // Asserted non-null first: two nulls would satisfy the comparison below
        // while proving nothing.
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

    // Unknown is not "too big". Warning on a number nobody has is worse than
    // staying quiet.
    [Fact]
    public void An_unmeasured_conversation_never_overflows()
    {
        Assert.False(ModelHolding(1).WouldOverflow(null));
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

// The estimator itself, where the arithmetic is worth pinning down. Pure, so none
// of this needs a session.
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

    // It travels as ChatOptions.Instructions rather than in the message list, which
    // is exactly why it is easy to forget — and it occupies the window regardless.
    [Fact]
    public void The_system_prompt_counts_toward_the_estimate()
    {
        var withoutPrompt = ContextTokenEstimator.Estimate([User("hei")], systemPrompt: null);
        var withPrompt = ContextTokenEstimator.Estimate([User("hei")], new string('s', 900));

        Assert.True(withPrompt > withoutPrompt);
    }

    // A tool result is routinely the largest thing in a conversation. An estimate
    // that walked only message text would miss it entirely and understate the one
    // conversation that actually overflows.
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
