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
    public void A_turn_still_running_or_answered_has_nothing_to_say()
    {
        Assert.Null(TurnNotice.Describe(TurnEnded(TurnStatus.Running)));
        Assert.Null(TurnNotice.Describe(TurnEnded(TurnStatus.Completed)));
    }

    [Fact]
    public void Every_other_ending_has_a_notice_of_its_own()
    {
        var notices = EndingsWorthSaying.Select(status => TurnNotice.Describe(TurnEnded(status, TurnStage.Generating))).ToList();

        Assert.All(notices, notice => Assert.False(string.IsNullOrWhiteSpace(notice)));
        Assert.Equal(notices.Count, notices.Distinct().Count());
    }

    [Fact]
    public void A_failed_turn_says_what_was_lost_at_the_stage_it_failed()
    {
        var notice = TurnNotice.Describe(TurnEnded(TurnStatus.Failed, TurnStage.SavingResponse));

        Assert.Equal(TurnStageNotice.Describe(TurnStage.SavingResponse), notice);
    }

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
