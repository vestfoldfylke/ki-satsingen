namespace kisatsingen.Data.Repositories;

public interface IDatabaseCleanupRepository
{
    Task<int> CleanupChats(TimeSpan interval, CancellationToken ct = default);
}
