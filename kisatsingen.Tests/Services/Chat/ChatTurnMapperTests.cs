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
                "Error: Function failed."),
            new TextSegment(Guid.NewGuid(), 1, "Det gikk ikke."));

        var restored = RoundTrip(turn);

        Assert.Equal(Describe(turn.Answer), Describe(restored.Answer));
    }

    [Fact]
    public void Norwegian_text_is_stored_as_written_rather_than_escaped()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith(new TextSegment(Guid.NewGuid(), 0, "Kjøretøy på vei")));

        Assert.Contains("Kjøretøy på vei", stored.AnswerJson);
    }

    [Fact]
    public void How_a_turn_ended_survives_the_round_trip()
    {
        var turn = TurnWith() with { Status = TurnStatus.Failed, FailedAt = TurnStage.Generating };

        var restored = RoundTrip(turn);

        Assert.Equal((TurnStatus.Failed, TurnStage.Generating), (restored.Status, restored.FailedAt));
    }

    [Fact]
    public void What_the_provider_reported_and_what_was_estimated_survive_the_round_trip_apart()
    {
        var metadata = new TurnMetadata("gpt-dated", "resp-1", "stop", new MessageUsage(900, 100, 1200, 40), 400, 50);

        var restored = RoundTrip(TurnWith() with { Metadata = metadata });

        Assert.Equal(metadata, restored.Metadata);
    }

    // One unreadable row must not lock the user out of the whole chat.
    [Theory]
    [InlineData("""[{"kind":"from-a-newer-build"}]""")]
    // System.Text.Json reports this one as NotSupportedException, not JsonException.
    [InlineData("""[{"id":"00000000-0000-0000-0000-000000000000","kind":"text","round":0,"text":"hei"}]""")]
    // Refused by name-only enum reading, so no status the view cannot handle gets through.
    [InlineData("""[{"kind":"tool","id":"00000000-0000-0000-0000-000000000000","round":0,"callId":"c","toolName":"t","arguments":{},"status":7,"result":null}]""")]
    public void An_answer_that_cannot_be_read_is_marked_unreadable_rather_than_failing_the_load(string answerJson)
    {
        var unreadable = Copy(ChatTurnMapper.ToEntity(TurnWith()), answerJson: answerJson);

        var restored = ChatTurnMapper.FromEntity(unreadable, DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

        Assert.True(restored.IsAnswerUnreadable);
        Assert.Empty(restored.Answer);
    }

    [Fact]
    public void An_answer_that_reads_is_not_marked_unreadable()
    {
        var restored = RoundTrip(TurnWith(new TextSegment(Guid.NewGuid(), 0, "hei")));

        Assert.False(restored.IsAnswerUnreadable);
    }

    [Fact]
    public void A_status_this_build_does_not_know_reads_as_unfinished()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());
        var unknown = Copy(stored, status: "SomethingNewer");

        var restored = ChatTurnMapper.FromEntity(unknown, DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

        Assert.Equal(TurnStatus.Unfinished, restored.Status);
    }

    [Fact]
    public void A_model_is_named_by_the_catalogue_when_the_turn_is_read()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());

        var restored = ChatTurnMapper.FromEntity(stored, DateTimeOffset.UtcNow, _ => "Named now", NullLogger.Instance);

        Assert.Equal("Named now", restored.ModelDisplayName);
    }

    // Its ending was never written: the process died, or the final save failed.
    [Fact]
    public void A_running_turn_loaded_long_after_it_started_reads_as_unfinished()
    {
        var started = DateTimeOffset.UtcNow;
        var stored = ChatTurnMapper.ToEntity(TurnWith() with { Status = TurnStatus.Running, StartedAt = started });

        var restored = ChatTurnMapper.FromEntity(stored, started + ChatTurnMapper.StillRunningElsewhereFor + TimeSpan.FromSeconds(1), key => key.Value, NullLogger.Instance);

        Assert.Equal(TurnStatus.Unfinished, restored.Status);
    }

    // A reload mid-answer: the circuit left behind may still be writing it.
    [Fact]
    public void A_running_turn_loaded_soon_after_it_started_is_still_running()
    {
        var started = DateTimeOffset.UtcNow;
        var stored = ChatTurnMapper.ToEntity(TurnWith() with { Status = TurnStatus.Running, StartedAt = started });

        var restored = ChatTurnMapper.FromEntity(stored, started + TimeSpan.FromSeconds(30), key => key.Value, NullLogger.Instance);

        Assert.Equal(TurnStatus.Running, restored.Status);
    }

    [Fact]
    public void A_turn_that_ended_reads_as_it_ended_however_long_ago()
    {
        var started = DateTimeOffset.UtcNow;
        var stored = ChatTurnMapper.ToEntity(TurnWith() with { Status = TurnStatus.Stopped, StartedAt = started });

        var restored = ChatTurnMapper.FromEntity(stored, started + TimeSpan.FromDays(30), key => key.Value, NullLogger.Instance);

        Assert.Equal(TurnStatus.Stopped, restored.Status);
    }

    [Fact]
    public void Saved_and_unavailable_attachments_survive_the_round_trip()
    {
        TurnAttachment[] attachments = [new("notat.md", FileId: Guid.NewGuid()), new("bilde.png", UnavailableReason: "Bildet kunne ikke leses.")];

        var restored = RoundTrip(TurnWith() with { Attachments = attachments });

        Assert.Equal(attachments, restored.Attachments);
    }

    // The stored format is name and file id or reason, nothing derived.
    [Fact]
    public void A_saved_attachment_is_stored_as_its_name_and_file_id_only()
    {
        var fileId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var stored = ChatTurnMapper.ToEntity(TurnWith() with { Attachments = [new("notat.md", fileId)] });

        Assert.Equal("""[{"fileName":"notat.md","fileId":"11111111-1111-1111-1111-111111111111"}]""", stored.AttachmentsJson);
    }

    [Fact]
    public void A_turn_without_attachments_stores_none()
    {
        var stored = ChatTurnMapper.ToEntity(TurnWith());

        Assert.Null(stored.AttachmentsJson);
    }

    // The prompt is then replayed without its attachment line, rather than the
    // chat failing to open.
    [Fact]
    public void Unreadable_attachments_read_back_as_none()
    {
        var stored = Copy(ChatTurnMapper.ToEntity(TurnWith()), attachmentsJson: "not json");

        var restored = ChatTurnMapper.FromEntity(stored, DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

        Assert.Empty(restored.Attachments);
    }

    [Fact]
    public void A_null_entry_in_the_stored_attachments_is_dropped()
    {
        var stored = Copy(ChatTurnMapper.ToEntity(TurnWith()), attachmentsJson: """[null,{"fileName":"notat.md","fileId":"11111111-1111-1111-1111-111111111111"}]""");

        var restored = ChatTurnMapper.FromEntity(stored, DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

        Assert.Equal(["notat.md"], restored.Attachments.Select(attachment => attachment.FileName));
    }

    private static Turn RoundTrip(Turn turn) =>
        ChatTurnMapper.FromEntity(ChatTurnMapper.ToEntity(turn), DateTimeOffset.UtcNow, key => key.Value, NullLogger.Instance);

    // JsonElement compares by reference, so segments are compared by their JSON.
    private static string Describe(IReadOnlyList<TurnSegment> answer) => JsonSerializer.Serialize(answer);

    private static kisatsingen.Data.Entities.ChatTurn Copy(
        kisatsingen.Data.Entities.ChatTurn stored,
        string? answerJson = null,
        string? status = null,
        string? attachmentsJson = null) => new()
    {
        Id = stored.Id,
        Prompt = stored.Prompt,
        SystemPrompt = stored.SystemPrompt,
        ModelKey = stored.ModelKey,
        Status = status ?? stored.Status,
        AnswerJson = answerJson ?? stored.AnswerJson,
        AttachmentsJson = attachmentsJson ?? stored.AttachmentsJson
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
