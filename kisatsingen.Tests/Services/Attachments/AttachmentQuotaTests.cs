using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

// The quota counts what is on disk, so these tests write real files of the
// sizes they need: ready ones for files already marked ready, uploading ones for
// uploads still in progress.
public sealed class AttachmentQuotaTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOwner = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const long Kilobyte = 1024;

    private readonly AttachmentTestEnvironment _environment = new(new AttachmentOptions
    {
        MaxPendingFilesPerUser = 2,
        MaxPendingBytesPerUser = 15 * Kilobyte,
        MaxPendingBytesPerInstance = 25 * Kilobyte
    });

    public void Dispose() => _environment.Dispose();

    private AttachmentQuota Quota => _environment.Quota;

    private UploadOutcome.Stored CreateUploadingFile(Guid owner, long size)
    {
        var (file, stream) = _environment.Storage.CreateFile(owner, Guid.NewGuid());
        using (stream)
        {
            stream.SetLength(size);
        }

        return new UploadOutcome.Stored(file, size, Sha256: "not read by the quota");
    }

    private TempFile CreateReadyFile(Guid owner, long size)
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

    // Two tabs of one owner, finishing one after the other at the last free
    // place.
    [Fact]
    public void Of_two_uploads_finishing_at_the_file_limit_the_second_is_refused()
    {
        CreateReadyFile(Owner, Kilobyte);
        var first = CreateUploadingFile(Owner, Kilobyte);
        var second = CreateUploadingFile(Owner, Kilobyte);

        var firstCheck = Quota.MarkReadyIfFits(Owner, first);
        var secondCheck = Quota.MarkReadyIfFits(Owner, second);

        Assert.IsType<QuotaCheck.Fits>(firstCheck);
        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.TooManyPending(2)), secondCheck);
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
        first.Dispose();

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
    public void A_file_finishing_after_the_attachment_folder_is_gone_is_refused_as_unavailable()
    {
        var stored = CreateUploadingFile(Owner, Kilobyte);
        Directory.Delete(_environment.Folder, recursive: true);

        var check = Quota.MarkReadyIfFits(Owner, stored);

        Assert.Equal(new QuotaCheck.Rejected(UploadRejections.Unavailable), check);
    }

    [Fact]
    public void A_negative_declared_size_is_refused_as_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Quota.CheckDeclaredSizeFits(Owner, -1));
    }
}
