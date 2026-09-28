using System.Text.Json;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatTurnMapperTests
{
    [Fact]
    public void An_answer_with_text_and_a_tool_survives_the_round_trip_through_its_entity()
    {
        var turn = TurnWith(
            new TextSegment(Guid.NewGuid(), 0, "Sjekker."),
            new ToolSegment(
                Guid.NewGuid(),
                0,
                "call-1",
                "search",
                JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["query"] = "vær" }),
                ToolStatus.Failed,
                JsonSerializer.SerializeToElement("Error: Function failed.")),
            new TextSegment(Guid.NewGuid(), 1, "Det gikk ikke."));

        var restored = RoundTrip(turn);

        Assert.Equal(Describe(turn.Answer), Describe(restored.Answer));
    }

    [Fact]
    public void How_a_turn_ended_survives_the_round_trip()
    {
        var turn = TurnWith() with { Status = TurnStatus.Failed, FailedAt = TurnStage.Generating };

        var restored = RoundTrip(turn);

        Assert.Equal((TurnStatus.Failed, TurnStage.Generating), (restored.Status, restored.FailedAt));
    }

    [Fact]
    public void What_the_provider_reported_survives_the_round_trip()
    {
        var metadata = new TurnMetadata("gpt-dated", "resp-1", "stop", new MessageUsage(1, 2, 3), 400, 50);

        var restored = RoundTrip(TurnWith() with { Metadata = metadata });

        Assert.Equal(metadata, restored.Metadata);
    }

    // One unreadable row must not lock the user out of the whole chat.
    [Fact]
    public void An_answer_that_cannot_be_read_is_shown_as_empty_rather_than_failing_the_load()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());
        var unreadable = Copy(stored, answerJson: """[{"kind":"from-a-newer-build"}]""");

        var restored = ChatTurnMapper.FromEntity(unreadable, key => key.Value, NullLogger.Instance);

        Assert.Empty(restored.Answer);
    }

    [Fact]
    public void A_status_this_build_does_not_know_reads_as_unfinished()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());
        var unknown = Copy(stored, status: "SomethingNewer");

        var restored = ChatTurnMapper.FromEntity(unknown, key => key.Value, NullLogger.Instance);

        Assert.Equal(TurnStatus.Unfinished, restored.Status);
    }

    [Fact]
    public void A_model_is_named_by_the_catalogue_when_the_turn_is_read()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());

        var restored = ChatTurnMapper.FromEntity(stored, _ => "Named now", NullLogger.Instance);

        Assert.Equal("Named now", restored.ModelDisplayName);
    }

    private static Turn RoundTrip(Turn turn) =>
        ChatTurnMapper.FromEntity(ChatTurnMapper.ToEntity(turn), key => key.Value, NullLogger.Instance);

    // JsonElement compares by reference, so segments are compared by their JSON.
    private static string Describe(IReadOnlyList<TurnSegment> answer) => JsonSerializer.Serialize(answer);

    private static kisatsingen.Data.Entities.ChatTurn Copy(
        kisatsingen.Data.Entities.ChatTurn stored,
        string? answerJson = null,
        string? status = null) => new()
    {
        Id = stored.Id,
        Prompt = stored.Prompt,
        SystemPrompt = stored.SystemPrompt,
        ModelKey = stored.ModelKey,
        Status = status ?? stored.Status,
        AnswerJson = answerJson ?? stored.AnswerJson
    };

    private static Turn TurnWith(params TurnSegment[] answer) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = "hei",
        SystemPrompt = "be brief",
        ModelKey = FakeChatModelCatalog.DefaultKey,
        ModelDisplayName = "Fast",
        StartedAt = DateTimeOffset.UtcNow,
        Answer = answer,
        Status = TurnStatus.Completed
    };
}
