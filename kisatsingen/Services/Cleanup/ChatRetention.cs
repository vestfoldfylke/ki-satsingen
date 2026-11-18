using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Cleanup;

public static class ChatRetention
{
    private const int DaysBeforeShowingDeletionMessage = 7;

    /// <param name="updatedAt">When the chat was last updated.</param>
    /// <param name="configuration">The chat cleanup configuration the background job runs with.</param>
    /// <param name="utcNow">The current time.</param>
    /// <param name="timeZone">The time zone the background job interprets <see cref="DatabaseChatCleanupConfiguration.TargetTime"/> in.</param>
    public static string? DescribeUpcomingDeletion(DateTimeOffset updatedAt, DatabaseChatCleanupConfiguration configuration, DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        if (!configuration.Enabled || configuration.Retention is not { } retention || configuration.TargetTime is not { } targetTime)
        {
            return null;
        }

        var deletionAt = GetScheduledDeletionDateTime(updatedAt, retention, targetTime, timeZone);

        if (deletionAt <= utcNow)
        {
            var daysOverdue = (int)Math.Floor((utcNow - deletionAt).TotalDays);

            return daysOverdue switch
            {
                0 => "Slettes automatisk nå",
                1 => "Skulle vært slettet for 1 dag siden",
                _ => $"Skulle vært slettet for {daysOverdue} dager siden"
            };
        }

        var daysUntilDeletion = (int)Math.Ceiling((deletionAt - utcNow).TotalDays);

        return daysUntilDeletion switch
        {
            1 => "Slettes automatisk innen 24 timer",
            <= DaysBeforeShowingDeletionMessage => $"Slettes automatisk innen {daysUntilDeletion} dager",
            _ => null
        };
    }

    // The job deletes a chat on its first run after the chat becomes eligible, which can be up to a day later.
    private static DateTimeOffset GetScheduledDeletionDateTime(DateTimeOffset updatedAt, TimeSpan retention, TimeSpan targetTime, TimeZoneInfo timeZone)
    {
        var eligibleAt = DatabaseCleanupRepository.GetEligibleForDeletionDateTime(updatedAt, retention);
        var eligibleAtLocal = TimeZoneInfo.ConvertTime(eligibleAt, timeZone).DateTime;

        return eligibleAt + DatabaseCleanupBackgroundService.CalculateDelayToNextOccurrence(eligibleAtLocal, targetTime);
    }
}
