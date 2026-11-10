using kisatsingen.Services;
using kisatsingen.Services.Cleanup;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

public class DatabaseCleanupRepository(IDbContextFactory<AppDbContext> factory, ILogger<DatabaseCleanupRepository> logger) : IDatabaseCleanupRepository
{
    public static Type[] AllowedCallers { get; } = [typeof(DatabaseCleanupBackgroundService)];

    public async Task<int> CleanupChats(TimeSpan interval, CancellationToken ct = default)
    {
        var retentionPeriod = GetRetentionDateTime(interval);
        await using var db = await factory.CreateDbContextAsync(ct);

        logger.LogInformation("Starting to clean up chats older than {RetentionPeriod}", DateTimeService.GetTimeZonedDateTimeOffsetString(retentionPeriod));

        try
        {
            var totalRowsDeleted = await db.Chats.Where(chat => chat.UpdatedAt < retentionPeriod).ExecuteDeleteAsync(ct);
            logger.LogInformation("Deleted {RowsDeletedCount} Chats with their corresponding ChatTurns", totalRowsDeleted);

            return totalRowsDeleted;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat cleanup operation was cancelled");

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Chat cleanup operation failed");

            return 0;
        }
    }

    private static DateTimeOffset GetRetentionDateTime(TimeSpan interval) => DateTimeOffset.UtcNow.Add(-interval);
}
