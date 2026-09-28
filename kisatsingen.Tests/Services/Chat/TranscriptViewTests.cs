using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TranscriptViewTests
{
    [Fact]
    public void Only_the_turn_this_session_is_streaming_is_live()
    {
        var earlier = TurnOn(FakeChatModelCatalog.DefaultKey);
        var streaming = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running };

        var views = TranscriptView.Build([earlier, streaming], streaming.Id);

        Assert.Equal([false, true], views.Select(view => view.IsLive));
    }

    // TranscriptTurn skips re-rendering on equality; a turn rebuilt on every
    // render would re-render the whole transcript on every tool event.
    [Fact]
    public void An_unchanged_turn_is_handed_to_the_page_as_the_same_view()
    {
        var turns = new[] { TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running } };

        var first = TranscriptView.Build(turns, liveTurnId: null);
        var second = TranscriptView.Build(turns, liveTurnId: null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_turn_on_a_different_model_than_the_one_before_announces_it()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(FakeChatModelCatalog.AlternativeKey, "Large")],
            liveTurnId: null);

        Assert.Equal([null, "Large"], views.Select(view => view.ModelChangedTo));
    }

    [Fact]
    public void A_turn_on_the_same_model_as_the_one_before_announces_nothing()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(FakeChatModelCatalog.DefaultKey)],
            liveTurnId: null);

        Assert.All(views, view => Assert.Null(view.ModelChangedTo));
    }

    // Unreadable is not a change of model, and neither side of one should pretend to be.
    [Fact]
    public void A_turn_whose_model_could_not_be_read_is_not_a_boundary()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(null), TurnOn(FakeChatModelCatalog.DefaultKey)],
            liveTurnId: null);

        Assert.All(views, view => Assert.Null(view.ModelChangedTo));
    }

    private static Turn TurnOn(ChatModelKey? modelKey, string? displayName = null) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = "hei",
        SystemPrompt = "be brief",
        ModelKey = modelKey,
        ModelDisplayName = displayName ?? modelKey?.Value,
        StartedAt = DateTimeOffset.UtcNow,
        Status = TurnStatus.Completed
    };
}
