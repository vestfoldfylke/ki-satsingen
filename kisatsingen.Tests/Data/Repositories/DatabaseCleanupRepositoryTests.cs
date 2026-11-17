using kisatsingen.Data.Repositories;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

public sealed class DatabaseCleanupRepositoryTests
{
    [Fact]
    public void The_cutoff_lies_the_retention_period_before_now()
    {
        var now = new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

        var cutoff = DatabaseCleanupRepository.GetCutoffDateTime(now, TimeSpan.FromDays(30));

        Assert.Equal(new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero), cutoff);
    }

    // The /chats deletion warning relies on these two staying inverses of each other.
    [Fact]
    public void The_cutoff_reaches_a_chat_exactly_when_the_chat_becomes_eligible_for_deletion()
    {
        var updatedAt = new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero);
        var retention = TimeSpan.FromDays(30);

        var eligibleAt = DatabaseCleanupRepository.GetEligibleForDeletionDateTime(updatedAt, retention);

        Assert.Equal(updatedAt, DatabaseCleanupRepository.GetCutoffDateTime(eligibleAt, retention));
    }
}
