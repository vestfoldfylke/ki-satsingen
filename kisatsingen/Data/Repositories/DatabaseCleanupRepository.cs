using kisatsingen.Services;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

// DatabaseCleanupRepositoryTests makes sure that only allowed callers have the
// implemented interface.
public class DatabaseCleanupRepository(IDbContextFactory<AppDbContext> factory, ILogger<DatabaseCleanupRepository> logger) : IDatabaseCleanupRepository
{
    public async Task<int> CleanupChats(TimeSpan interval, CancellationToken ct = default)
    {
        var retentionPeriod = GetRetentionDateTime(interval);
        await using var db = await factory.CreateDbContextAsync(ct);

        logger.LogInformation("Starting to clean up chats older than {RetentionPeriod}", DateTimeService.GetTimeZonedDateTimeOffsetString(retentionPeriod));

        var totalRowsDeleted = await db.Chats.Where(chat => chat.UpdatedAt < retentionPeriod).ExecuteDeleteAsync(ct);
        logger.LogInformation("Deleted {RowsDeletedCount} Chats with their corresponding ChatTurns", totalRowsDeleted);

        return totalRowsDeleted;
    }

    private static DateTimeOffset GetRetentionDateTime(TimeSpan interval) => DateTimeOffset.UtcNow.Add(-interval);
}
