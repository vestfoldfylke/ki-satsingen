using kisatsingen.Services;
using Microsoft.EntityFrameworkCore;

namespace kisatsingen.Data.Repositories;

// Not registered in DI on purpose; Program.cs builds it for DatabaseCleanupBackgroundService only.
public class DatabaseCleanupRepository(IDbContextFactory<AppDbContext> factory, ILogger<DatabaseCleanupRepository> logger) : IDatabaseCleanupRepository
{
    public async Task<int> CleanupChats(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = GetCutoffDateTime(DateTimeOffset.UtcNow, retention);
        await using var db = await factory.CreateDbContextAsync(ct);

        logger.LogInformation("Starting to clean up chats older than {Cutoff}", DateTimeService.GetTimeZonedDateTimeOffsetString(cutoff));

        var totalRowsDeleted = await db.Chats.Where(chat => chat.UpdatedAt < cutoff).ExecuteDeleteAsync(ct);
        logger.LogInformation("Deleted {RowsDeletedCount} Chats with their corresponding ChatTurns", totalRowsDeleted);

        return totalRowsDeleted;
    }

    internal static DateTimeOffset GetCutoffDateTime(DateTimeOffset now, TimeSpan retention) => now.Add(-retention);
}
