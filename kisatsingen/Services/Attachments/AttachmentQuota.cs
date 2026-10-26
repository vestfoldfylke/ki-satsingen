namespace kisatsingen.Services.Attachments;

// C# 14 has no discriminated unions: the private constructor means only the
// nested cases below can derive, so a check is always Fits or Rejected.
public abstract record QuotaCheck
{
    private QuotaCheck() { }

    public sealed record Fits : QuotaCheck;

    public sealed record Rejected(UploadRejection Reason) : QuotaCheck;
}

// How much pending attachments may hold: per owner across all their tabs, and
// per instance, since each instance's /tmp shares the host disk with every
// app in the App Service plan.
//
// It keeps no counts of its own: the temp attachments folder is the truth, and a file stops
// counting when it is deleted, including after it is processed. Two rules:
// - Starting is cautious: everything on disk counts, uploads still in
//   progress included, so a user cannot start more than their limits allow.
// - Finishing is fair: only files already ready count, plus this one, so a
//   finished file is never pushed out by an upload that may still fail.
public sealed class AttachmentQuota(AttachmentStorage storage, AttachmentOptions options)
{
    private readonly Lock _markReadyLock = new();

    // Before any bytes move, so an honest file that cannot fit is refused at
    // once. Only a hint: the declared size comes from the browser, and another
    // tab may start at the same moment, so MarkReadyIfFits decides.
    public QuotaCheck CheckDeclaredSizeFits(Guid ownerId, long declaredSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(declaredSizeBytes);

        var ownerUsage = storage.UsageOf(ownerId);
        var totalUsage = storage.TotalUsage();
        return CheckFits(
            ownerUsage.FileCount + 1,
            ownerUsage.ByteCount + declaredSizeBytes,
            totalUsage.ByteCount + declaredSizeBytes);
    }

    // Decides with the stored file at its real size, and marks it ready under
    // the same lock, so the next upload to finish already counts it: two
    // uploads finishing together cannot both slip past a limit, nor both be
    // refused when one would fit. A refused file is deleted here.
    public QuotaCheck MarkReadyIfFits(Guid ownerId, UploadOutcome.Stored stored)
    {
        lock (_markReadyLock)
        {
            var ownerReady = storage.ReadyUsageOf(ownerId);
            var totalReady = storage.TotalReadyUsage();
            var check = CheckFits(
                ownerReady.FileCount + 1,
                ownerReady.ByteCount + stored.SizeBytes,
                totalReady.ByteCount + stored.SizeBytes);

            if (check is QuotaCheck.Rejected)
            {
                stored.File.Dispose();
                return check;
            }

            if (!storage.TryMarkReady(stored.File))
            {
                stored.File.Dispose();
                return new QuotaCheck.Rejected(UploadRejections.Interrupted);
            }

            return check;
        }
    }

    // The owner's own limits are named first: the user can act on them ("send
    // or remove some"), but not on the instance's.
    private QuotaCheck CheckFits(int ownerFileCount, long ownerByteCount, long instanceByteCount)
    {
        if (ownerFileCount > options.MaxPendingFilesPerUser)
        {
            return new QuotaCheck.Rejected(UploadRejections.TooManyPending(options.MaxPendingFilesPerUser));
        }

        if (ownerByteCount > options.MaxPendingBytesPerUser)
        {
            return new QuotaCheck.Rejected(UploadRejections.TooManyPendingBytes(options.MaxPendingBytesPerUser));
        }

        if (instanceByteCount > options.MaxPendingBytesPerInstance)
        {
            return new QuotaCheck.Rejected(UploadRejections.InstanceBudgetFull);
        }

        return new QuotaCheck.Fits();
    }
}
