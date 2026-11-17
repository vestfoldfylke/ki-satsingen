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
    public void Return_Null_When_Cleanup_Is_Disabled()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = Retention };

        var description = Describe(ScheduledDeletion.AddHours(-1), configuration);

        Assert.Null(description);
    }

    [Fact]
    public void Return_Null_When_Deletion_Is_More_Than_Seven_Days_Away()
    {
        var utcNow = ScheduledDeletion.AddDays(-7).AddMinutes(-1);

        var description = Describe(utcNow);

        Assert.Null(description);
    }

    [Fact]
    public void Show_Seven_Days_When_Deletion_Is_Exactly_Seven_Days_Away()
    {
        var utcNow = ScheduledDeletion.AddDays(-7);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 7 dager", description);
    }

    [Fact]
    public void Round_Remaining_Days_Up_So_The_Label_Never_Promises_Deletion_Too_Early()
    {
        var utcNow = ScheduledDeletion.AddDays(-1.9);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 2 dager", description);
    }

    [Fact]
    public void Show_Within_24_Hours_When_Less_Than_A_Day_Remains()
    {
        var utcNow = ScheduledDeletion.AddHours(-23);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Count_To_The_Next_Scheduled_Run_Rather_Than_To_The_Retention_Cutoff()
    {
        var utcNow = UpdatedAt.Add(Retention).AddHours(-3);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 2 dager", description);
    }

    [Fact]
    public void Do_Not_Report_A_Chat_As_Overdue_While_It_Waits_For_The_Next_Run()
    {
        var utcNow = UpdatedAt.Add(Retention).AddHours(1);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Report_A_Chat_As_Overdue_When_The_Scheduled_Run_Has_Passed()
    {
        var utcNow = ScheduledDeletion.AddHours(1);

        var description = Describe(utcNow);

        Assert.Equal("Skulle vært slettet for 1 dag(er) siden", description);
    }

    [Fact]
    public void Round_Overdue_Days_Up()
    {
        var utcNow = ScheduledDeletion.AddDays(2.1);

        var description = Describe(utcNow);

        Assert.Equal("Skulle vært slettet for 3 dag(er) siden", description);
    }

    // The delete compares with a strict "<", so the run at the exact moment of eligibility leaves the chat alone.
    [Fact]
    public void Schedule_Deletion_For_The_Following_Run_When_The_Chat_Becomes_Eligible_Exactly_At_The_Target_Time()
    {
        var updatedAt = new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero);
        var utcNow = updatedAt.Add(Retention).AddMinutes(1);

        var description = ChatRetention.DescribeUpcomingDeletion(updatedAt, EnabledConfiguration, utcNow, TimeZoneInfo.Utc);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Interpret_The_Target_Time_In_The_Given_Time_Zone()
    {
        var oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
        var updatedAt = new DateTimeOffset(2026, 10, 11, 1, 30, 0, TimeSpan.Zero);
        var utcNow = new DateTimeOffset(2026, 11, 10, 2, 30, 0, TimeSpan.Zero);

        var description = ChatRetention.DescribeUpcomingDeletion(updatedAt, EnabledConfiguration, utcNow, oslo);

        Assert.Equal("Skulle vært slettet for 1 dag(er) siden", description);
    }
}
