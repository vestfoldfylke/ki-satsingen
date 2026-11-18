using kisatsingen.Services.Cleanup;
using Xunit;

namespace kisatsingen.Tests.Services.Cleanup;

public sealed class ChatRetentionTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly DatabaseChatCleanupConfiguration EnabledConfiguration = new()
    {
        Retention = Retention,
        TargetTime = new TimeSpan(3, 0, 0)
    };

    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EligibleAt = UpdatedAt.Add(Retention);

    private static string? Describe(DateTimeOffset utcNow, DatabaseChatCleanupConfiguration? configuration = null) =>
        ChatRetention.DescribeUpcomingDeletion(UpdatedAt, configuration ?? EnabledConfiguration, utcNow);

    [Fact]
    public void Return_null_when_cleanup_is_disabled()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = Retention };

        var description = Describe(EligibleAt.AddHours(-1), configuration);

        Assert.Null(description);
    }

    [Fact]
    public void Return_null_when_deletion_is_more_than_seven_days_away()
    {
        var utcNow = EligibleAt.AddDays(-7).AddMinutes(-1);

        var description = Describe(utcNow);

        Assert.Null(description);
    }

    [Fact]
    public void Show_seven_days_when_deletion_is_exactly_seven_days_away()
    {
        var utcNow = EligibleAt.AddDays(-7);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 7 dager", description);
    }

    [Fact]
    public void Round_remaining_days_up()
    {
        var utcNow = EligibleAt.AddDays(-1.9);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 2 dager", description);
    }

    [Fact]
    public void Show_within_24_hours_when_a_day_or_less_remains()
    {
        var utcNow = EligibleAt.AddHours(-23);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }

    [Fact]
    public void Show_within_24_hours_when_the_chat_is_already_eligible_for_deletion()
    {
        var utcNow = EligibleAt.AddDays(3);

        var description = Describe(utcNow);

        Assert.Equal("Slettes automatisk innen 24 timer", description);
    }
}
