using System.Text.Json;
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

    // Its ending was never written: the process died, or the final save failed.
    [Fact]
    public void A_running_turn_nothing_is_streaming_reads_as_unfinished()
    {
        var abandoned = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running };

        var view = Assert.Single(TranscriptView.Build([abandoned], liveTurnId: null));

        Assert.Equal(TurnStatus.Unfinished, view.Turn.Status);
    }

    [Fact]
    public void An_unfinished_turn_shows_a_tool_it_left_running_as_interrupted()
    {
        var tool = new ToolSegment(Guid.NewGuid(), 0, "call-1", "probe", JsonSerializer.SerializeToElement(new { }), ToolStatus.Running, null);
        var abandoned = TurnOn(FakeChatModelCatalog.DefaultKey) with { Status = TurnStatus.Running, Answer = [tool] };

        var view = Assert.Single(TranscriptView.Build([abandoned], liveTurnId: null));

        Assert.Equal(ToolStatus.Interrupted, Assert.IsType<ToolSegment>(Assert.Single(view.Turn.Answer)).Status);
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
