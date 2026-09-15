using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

// A lookup table, but one with a silent failure mode: add a stage and forget its
// notice and nothing breaks — no compiler error, no failing test — the user just
// starts getting a generic message where a specific one was intended. These
// tests exist to make that loud.
public sealed class TurnStageNoticeTests
{
    private static readonly TurnStage[] AllStages = Enum.GetValues<TurnStage>();

    [Fact]
    public void Every_stage_has_a_notice_of_its_own()
    {
        var notices = AllStages.Select(TurnStageNotice.Describe).ToList();

        Assert.Equal(notices.Count, notices.Distinct().Count());
    }

    [Fact]
    public void No_stage_falls_through_to_the_generic_message()
    {
        var generic = TurnStageNotice.Describe((TurnStage)(-1));

        Assert.DoesNotContain(generic, AllStages.Select(TurnStageNotice.Describe));
    }

    // Persisted and rendered as-is, so an empty or whitespace notice would show
    // the user a blank event rather than an explanation.
    [Fact]
    public void Every_notice_is_something_the_user_can_read()
    {
        var notices = AllStages.Select(TurnStageNotice.Describe);

        Assert.All(notices, notice => Assert.False(string.IsNullOrWhiteSpace(notice)));
    }
}
