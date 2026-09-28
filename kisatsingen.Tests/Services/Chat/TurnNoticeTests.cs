using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// Same silent failure mode as TurnStageNotice: add a status, forget its notice,
// and users get the generic message where a specific one was intended.
public sealed class TurnNoticeTests
{
    private static readonly TurnStatus[] EndingsWorthSaying =
        Enum.GetValues<TurnStatus>().Where(status => status is not (TurnStatus.Running or TurnStatus.Completed)).ToArray();

    [Fact]
    public void A_turn_streaming_here_or_answered_has_nothing_to_say()
    {
        Assert.Null(TurnNotice.Describe(Live(TurnEnded(TurnStatus.Running))));
        Assert.Null(TurnNotice.Describe(Seen(TurnEnded(TurnStatus.Completed))));
    }

    // Otherwise the user sees a question with nothing under it and no reason why.
    [Fact]
    public void A_turn_running_elsewhere_says_it_may_not_be_finished_or_saved()
    {
        Assert.NotNull(TurnNotice.Describe(Seen(TurnEnded(TurnStatus.Running))));
    }

    [Fact]
    public void Every_other_ending_has_a_notice_of_its_own()
    {
        var notices = EndingsWorthSaying.Select(status => TurnNotice.Describe(Seen(TurnEnded(status, TurnStage.Generating)))).ToList();

        Assert.All(notices, notice => Assert.False(string.IsNullOrWhiteSpace(notice)));
        Assert.Equal(notices.Count, notices.Distinct().Count());
    }

    [Fact]
    public void A_failed_turn_says_what_was_lost_at_the_stage_it_failed()
    {
        var notice = TurnNotice.Describe(Seen(TurnEnded(TurnStatus.Failed, TurnStage.SavingResponse)));

        Assert.Equal(TurnStageNotice.Describe(TurnStage.SavingResponse), notice);
    }

    [Fact]
    public void An_answer_that_could_not_be_read_says_so_whatever_the_turn_ended_as()
    {
        var notice = TurnNotice.Describe(Seen(TurnEnded(TurnStatus.Completed) with { IsAnswerUnreadable = true }));

        Assert.Equal("Svaret kunne ikke leses", notice);
    }

    private static TurnView Live(Turn turn) => new(turn, IsLive: true, ModelChangedTo: null);

    private static TurnView Seen(Turn turn) => new(turn, IsLive: false, ModelChangedTo: null);

    private static Turn TurnEnded(TurnStatus status, TurnStage? failedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Prompt = "hei",
        SystemPrompt = "be brief",
        ModelKey = FakeChatModelCatalog.DefaultKey,
        ModelDisplayName = "Fast",
        StartedAt = DateTimeOffset.UtcNow,
        Status = status,
        FailedAt = failedAt
    };
}
