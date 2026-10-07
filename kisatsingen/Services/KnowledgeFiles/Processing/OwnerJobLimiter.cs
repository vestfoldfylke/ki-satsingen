namespace kisatsingen.Services.KnowledgeFiles.Processing;

// At most N jobs per owner in the queue or running, across all their tabs.
// A semaphore per owner exists only while someone holds or awaits it, so the
// set of owners seen since startup does not accumulate.
internal sealed class OwnerJobLimiter(int maxJobsPerOwner)
{
    private readonly Lock _ownersLock = new();
    private readonly Dictionary<string, OwnerSlots> _owners = [];

    public async Task<IDisposable> AcquireAsync(string ownerId, CancellationToken ct)
    {
        OwnerSlots slots;
        lock (_ownersLock)
        {
            if (!_owners.TryGetValue(ownerId, out slots!))
            {
                slots = new OwnerSlots(maxJobsPerOwner);
                _owners.Add(ownerId, slots);
            }

            slots.Interested++;
        }

        try
        {
            await slots.Semaphore.WaitAsync(ct);
        }
        catch
        {
            Leave(ownerId, slots);
            throw;
        }

        return new Slot(() =>
        {
            slots.Semaphore.Release();
            Leave(ownerId, slots);
        });
    }

    private void Leave(string ownerId, OwnerSlots slots)
    {
        lock (_ownersLock)
        {
            if (--slots.Interested == 0)
            {
                _owners.Remove(ownerId);
                slots.Semaphore.Dispose();
            }
        }
    }

    // Holders and waiters alike, so the semaphore outlives everyone using it.
    private sealed class OwnerSlots(int capacity)
    {
        public SemaphoreSlim Semaphore { get; } = new(capacity, capacity);
        public int Interested { get; set; }
    }

    // Released once however often it is disposed: a job's slot is disposed on
    // whichever path finishes the job, and more than one may try.
    private sealed class Slot(Action release) : IDisposable
    {
        private int _isReleased;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isReleased, 1) == 0)
            {
                release();
            }
        }
    }
}
