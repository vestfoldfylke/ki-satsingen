using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Cleanup;

public static class ChatRetention
{
    private const int DaysBeforeShowingDeletionMessage = 7;

    /// <param name="updatedAt">When the chat was last updated.</param>
    /// <param name="configuration">The chat cleanup configuration the background job runs with.</param>
    /// <param name="utcNow">The current time.</param>
    public static string? DescribeUpcomingDeletion(DateTimeOffset updatedAt, DatabaseChatCleanupConfiguration configuration, DateTimeOffset utcNow)
    {
        if (!configuration.Enabled || configuration.Retention is not { } retention)
        {
            return null;
        }

        var eligibleAt = DatabaseCleanupRepository.GetEligibleForDeletionDateTime(updatedAt, retention);
        var daysUntilDeletion = (int)Math.Ceiling((eligibleAt - utcNow).TotalDays);

        return daysUntilDeletion switch
        {
            <= 1 => "Slettes automatisk innen 24 timer",
            <= DaysBeforeShowingDeletionMessage => $"Slettes automatisk innen {daysUntilDeletion} dager",
            _ => null
        };
    }
}
