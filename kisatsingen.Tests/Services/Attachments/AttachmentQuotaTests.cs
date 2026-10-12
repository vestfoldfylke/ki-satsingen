using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

// The quota counts what is on disk, so these tests write real files of the
// sizes they need: ready ones for files already marked ready, uploading ones for
// uploads still in progress.
public sealed class AttachmentQuotaTests : IDisposable
{
    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";
    private const long Kilobyte = 1024;

    private readonly AttachmentTestEnvironment _environment = new(new AttachmentOptions
    {
        MaxPendingFilesPerUser = 2,
        MaxPendingBytesPerUser = 15 * Kilobyte,
        MaxPendingBytesPerInstance = 25 * Kilobyte
    });

    public void Dispose() => _environment.Dispose();

    private AttachmentQuota Quota => _environment.Quota;

    private UploadOutcome.Stored CreateUploadingFile(string owner, long size)
    {
        var (file, stream) = _environment.Storage.Create(owner, Guid.NewGuid());
        using (stream)
        {
            stream.SetLength(size);
        }

        return new UploadOutcome.Stored(file, size, UploadHashes.Unique());
    }

    private TempFile CreateReadyFile(string owner, long size)
    {
        var stored = CreateUploadingFile(owner, size);
        _environment.Storage.TryMarkReady(stored.File);
        return stored.File;
    }

    [Fact]
    public void A_file_that_fits_is_marked_ready()
    {
        var stored = CreateUploadingFile(Owner, 10 * Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, stored);

        Assert.IsType<QuotaCheck.Fits>(check);
        Assert.Equal(new StorageUsage(1, 10 * Kilobyte), _environment.Storage.ReadyUsageOf(Owner));
    }

    [Fact]
    public void A_file_over_the_owners_file_limit_is_rejected_and_deleted()
    {
        CreateReadyFile(Owner, Kilobyte);
        CreateReadyFile(Owner, Kilobyte);
        var third = CreateUploadingFile(Owner, Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, third);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.TooManyPending(2)), check);
        Assert.False(File.Exists(third.File.Path));
    }

    [Fact]
    public void A_file_over_the_owners_byte_limit_is_rejected_with_the_owners_limit()
    {
        CreateReadyFile(Owner, 10 * Kilobyte);
        var second = CreateUploadingFile(Owner, 10 * Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, second);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.TooManyPendingBytes(15 * Kilobyte)), check);
    }

    [Fact]
    public void A_file_over_the_instance_budget_is_rejected_even_when_its_owner_has_room()
    {
        CreateReadyFile(OtherOwner, 14 * Kilobyte);
        var stored = CreateUploadingFile(Owner, 12 * Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, stored);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.InstanceBudgetFull), check);
    }

    [Fact]
    public void When_both_limits_are_exceeded_the_owners_limit_is_named()
    {
        CreateReadyFile(OtherOwner, 10 * Kilobyte);
        CreateReadyFile(Owner, 10 * Kilobyte);
        var second = CreateUploadingFile(Owner, 10 * Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, second);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.TooManyPendingBytes(15 * Kilobyte)), check);
    }

    [Fact]
    public void An_upload_still_in_progress_does_not_push_a_finished_file_out()
    {
        CreateUploadingFile(Owner, 10 * Kilobyte);
        var finished = CreateUploadingFile(Owner, 10 * Kilobyte);

        var check = Quota.MarkReadyIfFits(Owner, finished);

        Assert.IsType<QuotaCheck.Fits>(check);
    }

    [Fact]
    public void An_upload_still_in_progress_counts_when_another_upload_starts()
    {
        CreateUploadingFile(Owner, 10 * Kilobyte);

        var check = Quota.CheckDeclaredSizeFits(Owner, 10 * Kilobyte);

        Assert.IsType<QuotaCheck.Rejected>(check);
    }

    [Fact]
    public void A_declared_size_is_checked_against_what_is_already_on_disk()
    {
        CreateReadyFile(Owner, 10 * Kilobyte);

        var check = Quota.CheckDeclaredSizeFits(Owner, 10 * Kilobyte);

        Assert.IsType<QuotaCheck.Rejected>(check);
    }

    [Fact]
    public void Checking_a_declared_size_writes_nothing()
    {
        Quota.CheckDeclaredSizeFits(Owner, 10 * Kilobyte);

        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public void A_declared_size_counts_as_one_more_file()
    {
        CreateReadyFile(Owner, Kilobyte);
        CreateReadyFile(Owner, Kilobyte);

        var check = Quota.CheckDeclaredSizeFits(Owner, Kilobyte);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.TooManyPending(2)), check);
    }

    [Fact]
    public void A_deleted_file_frees_its_room()
    {
        var first = CreateReadyFile(Owner, 10 * Kilobyte);
        _environment.Storage.Delete(first);

        var check = Quota.CheckDeclaredSizeFits(Owner, 10 * Kilobyte);

        Assert.IsType<QuotaCheck.Fits>(check);
    }

    [Fact]
    public void One_owner_at_their_limit_does_not_affect_another()
    {
        CreateReadyFile(Owner, 15 * Kilobyte);

        var check = Quota.CheckDeclaredSizeFits(OtherOwner, 5 * Kilobyte);

        Assert.IsType<QuotaCheck.Fits>(check);
    }

    [Fact]
    public void A_negative_declared_size_is_refused_as_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Quota.CheckDeclaredSizeFits(Owner, -1));
    }
}
