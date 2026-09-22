using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// The size of a conversation, and whether a model can still hold it. The whole
// point of measuring it separately from ConversationUsage is that the two answer
// different questions and only one of them is bounded by the context window.
public sealed class ContextWindowTests
{
    [Fact]
    public async Task A_conversation_that_has_not_been_answered_has_no_measured_size()
    {
        await using var harness = new ChatSessionHarness();

        Assert.Null(harness.Session.EstimatedContextTokens);
    }

    [Fact]
    public async Task The_size_is_the_last_turns_input_plus_its_own_output()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("hei", inputTokens: 900, outputTokens: 100);

        await harness.Session.SendAsync("hei");

        Assert.Equal(1_000, harness.Session.EstimatedContextTokens);
    }

    // The one that matters. Each turn's input already contains the whole history,
    // so adding turns together counts it again and again — a conversation of 1_200
    // tokens would measure 1_800 after two turns and keep climbing. That is the
    // right answer for cost and the wrong one for a context warning.
    [Fact]
    public async Task The_size_is_the_last_turn_alone_not_every_turn_added_up()
    {
        await using var harness = new ChatSessionHarness();
        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("hei", inputTokens: 500, outputTokens: 100);
        await harness.Session.SendAsync("først");

        harness.Client.OnStream = _ => ModelStream.AnsweringWithUsage("hei", inputTokens: 1_100, outputTokens: 100);
        await harness.Session.SendAsync("så");

        Assert.Equal(1_200, harness.Session.EstimatedContextTokens);
        Assert.NotEqual(harness.Session.ConversationUsage?.TotalTokens, harness.Session.EstimatedContextTokens);
    }

    // A provider is not obliged to report usage on a streamed response. Mistral's
    // OpenAI-compatible endpoint is the live question here.
    [Fact]
    public async Task A_provider_that_reports_no_usage_leaves_the_size_unknown()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SendAsync("hei");

        Assert.Null(harness.Session.EstimatedContextTokens);
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
    // staying quiet, and it would fire on every chat with a provider that does not
    // report usage.
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
