namespace kisatsingen.Data.Repositories;

public interface IDatabaseCleanupRepository
{
    Task<int> CleanupChats(TimeSpan retention, CancellationToken ct = default);
}
