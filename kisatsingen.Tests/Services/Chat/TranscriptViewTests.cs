using System.Text.Json;
using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class TranscriptViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now - TranscriptView.StillRunningElsewhereFor - TimeSpan.FromSeconds(1);
    private static readonly DateTimeOffset JustNow = Now - TimeSpan.FromSeconds(30);

    [Fact]
    public void Only_the_turn_this_session_is_streaming_is_live()
    {
        var earlier = TurnOn(FakeChatModelCatalog.DefaultKey);
        var streaming = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running };

        var views = TranscriptView.Build([earlier, streaming], streaming.Id, Now);

        Assert.Equal([false, true], views.Select(view => view.IsLive));
    }

    // Its ending was never written: the process died, or the final save failed.
    [Fact]
    public void A_running_turn_long_past_any_answer_reads_as_unfinished()
    {
        var abandoned = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running, StartedAt = LongAgo };

        var view = Assert.Single(TranscriptView.Build([abandoned], liveTurnId: null, Now));

        Assert.Equal(TurnStatus.Unfinished, view.Turn.Status);
    }

    // A reload mid-answer: the circuit left behind is still writing it.
    [Fact]
    public void A_recent_running_turn_another_circuit_may_be_answering_is_still_running()
    {
        var elsewhere = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running, StartedAt = JustNow };

        var view = Assert.Single(TranscriptView.Build([elsewhere], liveTurnId: null, Now));

        Assert.Equal((TurnStatus.Running, false), (view.Turn.Status, view.IsLive));
    }

    [Fact]
    public void An_unfinished_turn_shows_a_tool_it_left_running_as_interrupted()
    {
        var tool = new ToolSegment(Guid.NewGuid(), 0, "call-1", "probe", JsonSerializer.SerializeToElement(new { }), ToolStatus.Running, null);
        var abandoned = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running, StartedAt = LongAgo, Answer = [tool] };

        var view = Assert.Single(TranscriptView.Build([abandoned], liveTurnId: null, Now));

        Assert.Equal(ToolStatus.Interrupted, Assert.IsType<ToolSegment>(Assert.Single(view.Turn.Answer)).Status);
    }

    // TranscriptTurn skips re-rendering on equality; a turn rebuilt on every
    // render would re-render the whole transcript on every tool event.
    [Fact]
    public void An_unchanged_turn_is_handed_to_the_page_as_the_same_view()
    {
        var turns = new[] { TurnOn(FakeChatModelCatalog.DefaultKey) };

        var first = TranscriptView.Build(turns, liveTurnId: null, Now);
        var second = TranscriptView.Build(turns, liveTurnId: null, Now);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_turn_on_a_different_model_than_the_one_before_announces_it()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(FakeChatModelCatalog.AlternativeKey, "Large")],
            liveTurnId: null,
            Now);

        Assert.Equal([null, "Large"], views.Select(view => view.ModelChangedTo));
    }

    [Fact]
    public void A_turn_on_the_same_model_as_the_one_before_announces_nothing()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(FakeChatModelCatalog.DefaultKey)],
            liveTurnId: null,
            Now);

        Assert.All(views, view => Assert.Null(view.ModelChangedTo));
    }

    // Unreadable is not a change of model, and neither side of one should pretend to be.
    [Fact]
    public void A_turn_whose_model_could_not_be_read_is_not_a_boundary()
    {
        var views = TranscriptView.Build(
            [TurnOn(FakeChatModelCatalog.DefaultKey), TurnOn(null), TurnOn(FakeChatModelCatalog.DefaultKey)],
            liveTurnId: null,
            Now);

        Assert.All(views, view => Assert.Null(view.ModelChangedTo));
    }

    private static Turn TurnOn(ChatModelKey? modelKey, string? displayName = null) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = "hei",
        SystemPrompt = "be brief",
        ModelKey = modelKey,
        ModelDisplayName = displayName ?? modelKey?.Value,
        StartedAt = JustNow,
        Status = TurnStatus.Completed
    };
}
