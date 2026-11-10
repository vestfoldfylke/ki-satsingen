using kisatsingen.Services.Cleanup;
using Xunit;

namespace kisatsingen.Tests.Services.Cleanup;

public sealed class DatabaseCleanupBackgroundServiceTests
{
    private static readonly TimeSpan ThreeAm = new(3, 0, 0);

    [Fact]
    public void A_target_time_later_today_runs_today()
    {
        var now = new DateTime(2026, 11, 10, 1, 30, 0);

        var delay = DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(now, ThreeAm);

        Assert.Equal(TimeSpan.FromMinutes(90), delay);
    }

    [Fact]
    public void A_target_time_already_passed_today_runs_tomorrow()
    {
        var now = new DateTime(2026, 11, 10, 14, 0, 0);

        var delay = DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(now, ThreeAm);

        Assert.Equal(TimeSpan.FromHours(13), delay);
    }

    // Right after a run finishes the clock can still read the target time, which must not trigger a second run.
    [Fact]
    public void Being_exactly_at_the_target_time_schedules_the_next_run_a_full_day_later()
    {
        var now = new DateTime(2026, 11, 10, 3, 0, 0);

        var delay = DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(now, ThreeAm);

        Assert.Equal(TimeSpan.FromDays(1), delay);
    }

    [Fact]
    public void A_midnight_target_time_runs_at_the_coming_midnight()
    {
        var now = new DateTime(2026, 11, 10, 23, 30, 0);

        var delay = DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(now, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(30), delay);
    }

    [Fact]
    public void The_last_second_of_the_day_as_target_time_runs_the_same_day()
    {
        var now = new DateTime(2026, 11, 10, 23, 0, 0);

        var delay = DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(now, new TimeSpan(23, 59, 59));

        Assert.Equal(new TimeSpan(0, 59, 59), delay);
    }
}
