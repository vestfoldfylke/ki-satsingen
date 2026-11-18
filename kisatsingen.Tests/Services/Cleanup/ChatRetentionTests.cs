using kisatsingen.Services.Cleanup;
using Xunit;

namespace kisatsingen.Tests.Services.Cleanup;

public sealed class ChatRetentionTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan ThreeAm = new(3, 0, 0);

    private static readonly DatabaseChatCleanupConfiguration EnabledConfiguration = new()
    {
        Retention = Retention,
        TargetTime = ThreeAm
    };

    // A chat updated at this moment becomes eligible for deletion 30 days later, at 2026-11-10 04:00 UTC,
    // and is deleted by the 03:00 run on 2026-11-11.
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ScheduledDeletion = new(2026, 11, 11, 3, 0, 0, TimeSpan.Zero);

    private static string? Describe(DateTimeOffset utcNow, DatabaseChatCleanupConfiguration? configuration = null) =>
        ChatRetention.DescribeUpcomingDeletion(UpdatedAt, configuration ?? EnabledConfiguration, utcNow, TimeZoneInfo.Utc);

    [Fact]
    public void Return_null_when_cleanup_is_disabled()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = Retention };

        var description = Describe(ScheduledDeletion.AddHours(-1), configuration);

        Assert.Null(description);
    }

    [Fact]
    public void Return_null_when_deletion_is_more_than_seven_days_away()
    {
        var utcNow = ScheduledDeletion.AddDays(-7).AddMinutes(-1);

        var description = Describe(utcNow);

        Assert.Null(description);
    }

    [Fact]
    public void Show_seven_days_when_deletion_is_exactly_seven_days_away()
    {
        var utcNow = ScheduledDeletion.AddDays(-7);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 7 dager", description);
    }

    [Fact]
    public void Round_remaining_days_up_so_the_label_never_promises_deletion_too_early()
    {
        var utcNow = ScheduledDeletion.AddDays(-1.9);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 2 dager", description);
    }

    [Fact]
    public void Show_within_24_hours_when_less_than_a_day_remains()
    {
        var utcNow = ScheduledDeletion.AddHours(-23);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Count_to_the_next_scheduled_run_rather_than_to_the_retention_cutoff()
    {
        var utcNow = UpdatedAt.Add(Retention).AddHours(-3);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 2 dager", description);
    }

    [Fact]
    public void Do_not_report_a_chat_as_overdue_while_it_waits_for_the_next_run()
    {
        var utcNow = UpdatedAt.Add(Retention).AddHours(1);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Report_deletion_as_happening_now_within_a_day_after_the_scheduled_run()
    {
        var utcNow = ScheduledDeletion.AddHours(1);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk nå", description);
    }

    [Fact]
    public void Report_a_single_overdue_day_in_singular()
    {
        var utcNow = ScheduledDeletion.AddDays(1.5);

        var description = Describe(utcNow);

        Assert.Equal("Skulle vært slettet for 1 dag siden", description);
    }

    [Fact]
    public void Round_overdue_days_down()
    {
        var utcNow = ScheduledDeletion.AddDays(2.9);

        var description = Describe(utcNow);

        Assert.Equal("Skulle vært slettet for 2 dager siden", description);
    }

    // The delete compares with a strict "<", so the run at the exact moment of eligibility leaves the chat alone.
    [Fact]
    public void Schedule_deletion_for_the_following_run_when_the_chat_becomes_eligible_exactly_at_the_target_time()
    {
        var updatedAt = new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero);
        var utcNow = updatedAt.Add(Retention).AddMinutes(1);

        var description = ChatRetention.DescribeUpcomingDeletion(updatedAt, EnabledConfiguration, utcNow, TimeZoneInfo.Utc);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Interpret_the_target_time_in_the_given_time_zone()
    {
        var oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
        var updatedAt = new DateTimeOffset(2026, 10, 11, 1, 30, 0, TimeSpan.Zero);
        var utcNow = new DateTimeOffset(2026, 11, 10, 2, 30, 0, TimeSpan.Zero);

        var description = ChatRetention.DescribeUpcomingDeletion(updatedAt, EnabledConfiguration, utcNow, oslo);

        Assert.Equal("Slettes automatisk nå", description);
    }
}
