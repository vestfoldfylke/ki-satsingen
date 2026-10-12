using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class PendingAttachmentRegistryTests : IDisposable
{
    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";
    private const long Kilobyte = 1024;

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly Guid _scopeKey = Guid.NewGuid();

    public void Dispose() => _environment.Dispose();

    private PendingAttachmentRegistry Registry => _environment.Registry;

    private static AttachmentTestEnvironment WithOptions(AttachmentOptions options) => new(options);

    private UploadReservation Reserve(PendingAttachmentRegistry registry, string owner = Owner, Guid? scopeKey = null, long size = Kilobyte) =>
        registry.Reserve(owner, scopeKey ?? _scopeKey, "notat.md", size) ?? throw new InvalidOperationException("Reservation refused.");

    // Unique content unless a test names it, so only a test about duplicates
    // has any. The file really has the size, because the quota measures what
    // is on disk.
    private static UploadOutcome.Stored StoreFile(AttachmentTestEnvironment environment, long size, string? sha256 = null)
    {
        var (file, stream) = environment.Storage.CreateFile(Owner, Guid.NewGuid());
        using (stream)
        {
            stream.SetLength(size);
        }

        return new UploadOutcome.Stored(file, size, sha256 ?? UploadHashes.Unique());
    }

    private void CompleteUpload(AttachmentTestEnvironment environment, Guid? scopeKey, string sha256, long size = Kilobyte)
    {
        var reservation = Reserve(environment.Registry, scopeKey: scopeKey, size: size);
        environment.Registry.TryStartUpload(reservation.AttachmentId);
        environment.Registry.Complete(reservation.AttachmentId, StoreFile(environment, size, sha256));
    }

    [Fact]
    public void A_file_with_an_extension_that_is_not_allowed_is_listed_as_refused_with_the_reason()
    {
        var reservation = Registry.Reserve(Owner, _scopeKey, "program.exe", Kilobyte);

        Assert.Null(reservation);
        var listed = Assert.Single(Registry.List(Owner, _scopeKey));
        Assert.Equal((AttachmentStatus.Rejected, UploadRejections.UnsupportedType), (listed.Status, listed.RejectionReason));
    }

    // Refusals count towards no limit, so picking bad files over and over must
    // not grow memory without bound.
    [Fact]
    public void Refused_files_are_capped_per_scope_and_the_newest_are_kept()
    {
        var time = new ManualTime();
        using var environment = new AttachmentTestEnvironment(time: time);
        foreach (var index in Enumerable.Range(0, 15))
        {
            environment.Registry.Reserve(Owner, _scopeKey, $"program{index}.exe", Kilobyte);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var listed = environment.Registry.List(Owner, _scopeKey);

        Assert.Equal(Enumerable.Range(5, 10).Select(index => $"program{index}.exe"), listed.Select(attachment => attachment.FileName));
    }

    [Fact]
    public void A_file_declared_over_the_size_limit_is_refused_before_any_bytes_are_read()
    {
        var reservation = Registry.Reserve(Owner, _scopeKey, "notat.md", _environment.Options.MaxFileBytes + 1);

        Assert.Null(reservation);
    }

    // Only a tampered browser declares one; in the early quota check it would
    // offset the bytes already pending.
    [Fact]
    public void A_file_declaring_a_negative_size_is_refused()
    {
        var reservation = Registry.Reserve(Owner, _scopeKey, "notat.md", -1);

        Assert.Null(reservation);
        Assert.Equal(UploadRejections.UnreadableSize, Assert.Single(Registry.List(Owner, _scopeKey)).RejectionReason);
    }

    [Fact]
    public void The_pending_file_limit_counts_one_owners_files_across_all_their_scopes_before_an_upload_starts()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        CompleteUpload(environment, Guid.NewGuid(), UploadHashes.Unique());
        var second = Reserve(environment.Registry);

        var isStarted = environment.Registry.TryStartUpload(second.AttachmentId);

        Assert.False(isStarted);
    }

    [Fact]
    public void Refused_files_do_not_count_towards_the_pending_file_limit()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        environment.Registry.Reserve(Owner, _scopeKey, "program.exe", Kilobyte);
        var valid = Reserve(environment.Registry);

        var isStarted = environment.Registry.TryStartUpload(valid.AttachmentId);

        Assert.True(isStarted);
    }

    [Fact]
    public void A_file_whose_declared_size_would_take_the_owner_over_the_pending_bytes_limit_is_refused_before_its_upload_starts()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxFileBytes = 10 * Kilobyte, MaxPendingBytesPerUser = 15 * Kilobyte });
        CompleteUpload(environment, _scopeKey, UploadHashes.Unique(), size: 10 * Kilobyte);
        var second = Reserve(environment.Registry, size: 10 * Kilobyte);

        var isStarted = environment.Registry.TryStartUpload(second.AttachmentId);

        Assert.False(isStarted);
        Assert.Equal(AttachmentStatus.Rejected, environment.Registry.List(Owner, _scopeKey).Single(listed => listed.AttachmentId == second.AttachmentId).Status);
    }

    // The declared size is only a hint; completion counts the real one.
    [Fact]
    public void A_file_larger_than_it_declared_is_refused_on_completion_and_deleted()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxFileBytes = 10 * Kilobyte, MaxPendingBytesPerUser = 15 * Kilobyte });
        var first = Reserve(environment.Registry, size: 10 * Kilobyte);
        environment.Registry.TryStartUpload(first.AttachmentId);
        environment.Registry.Complete(first.AttachmentId, StoreFile(environment, 10 * Kilobyte));
        var liar = Reserve(environment.Registry, size: 1);
        environment.Registry.TryStartUpload(liar.AttachmentId);
        var stored = StoreFile(environment, 10 * Kilobyte);

        environment.Registry.Complete(liar.AttachmentId, stored);

        Assert.Equal(AttachmentStatus.Rejected, environment.Registry.List(Owner, _scopeKey).Single(listed => listed.AttachmentId == liar.AttachmentId).Status);
        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void Removing_a_ready_attachment_frees_its_room_in_the_quota()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        CompleteUpload(environment, _scopeKey, UploadHashes.Unique());
        environment.Registry.Remove(Owner, Assert.Single(environment.Registry.List(Owner, _scopeKey)).AttachmentId);
        var second = Reserve(environment.Registry);

        var isStarted = environment.Registry.TryStartUpload(second.AttachmentId);

        Assert.True(isStarted);
    }

    // A sent file is still on disk until processing deletes it, so it still
    // counts.
    [Fact]
    public void A_file_taken_at_send_counts_until_its_file_is_deleted()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        CompleteUpload(environment, _scopeKey, UploadHashes.Unique());
        var taken = Assert.Single(environment.Registry.TakeReady(Owner, _scopeKey, environment.Registry.List(Owner, _scopeKey).Select(listed => listed.AttachmentId).ToHashSet()));
        var whileTaken = Reserve(environment.Registry, scopeKey: Guid.NewGuid());
        var afterDelete = Reserve(environment.Registry, scopeKey: Guid.NewGuid());

        var isStartedWhileTaken = environment.Registry.TryStartUpload(whileTaken.AttachmentId);
        taken.File.Dispose();
        var isStartedAfterDelete = environment.Registry.TryStartUpload(afterDelete.AttachmentId);

        Assert.Equal((false, true), (isStartedWhileTaken, isStartedAfterDelete));
    }

    // Both started while nothing was on disk; the quota decides when they
    // finish, with both files really there.
    [Fact]
    public void Of_two_uploads_that_finish_at_the_file_limit_exactly_one_becomes_ready()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        var first = Reserve(environment.Registry);
        var second = Reserve(environment.Registry, scopeKey: Guid.NewGuid());
        environment.Registry.TryStartUpload(first.AttachmentId);
        environment.Registry.TryStartUpload(second.AttachmentId);
        var firstFile = StoreFile(environment, Kilobyte);
        var secondFile = StoreFile(environment, Kilobyte);

        environment.Registry.Complete(first.AttachmentId, firstFile);
        environment.Registry.Complete(second.AttachmentId, secondFile);

        Assert.Equal(1, environment.Storage.UsageOf(Owner).FileCount);
    }

    // The scope key is not a secret, so the owner is what isolates users.
    [Fact]
    public void Another_owner_using_the_same_scope_key_sees_nothing()
    {
        Reserve(Registry);

        Assert.Empty(Registry.List(OtherOwner, _scopeKey));
    }

    [Fact]
    public void Another_owner_cannot_remove_an_attachment()
    {
        var reservation = Reserve(Registry);

        var isRemoved = Registry.Remove(OtherOwner, reservation.AttachmentId);

        Assert.False(isRemoved);
    }

    [Fact]
    public void Removing_an_attachment_cancels_its_upload()
    {
        var reservation = Reserve(Registry);

        Registry.Remove(Owner, reservation.AttachmentId);

        Assert.True(reservation.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Removing_a_ready_attachment_deletes_its_temp_file()
    {
        var reservation = Reserve(Registry);
        Registry.TryStartUpload(reservation.AttachmentId);
        var stored = StoreFile(_environment, Kilobyte);
        Registry.Complete(reservation.AttachmentId, stored);

        Registry.Remove(Owner, reservation.AttachmentId);

        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void A_file_completed_after_its_attachment_was_removed_is_deleted()
    {
        var reservation = Reserve(Registry);
        Registry.TryStartUpload(reservation.AttachmentId);
        Registry.Remove(Owner, reservation.AttachmentId);
        var stored = StoreFile(_environment, Kilobyte);

        Registry.Complete(reservation.AttachmentId, stored);

        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void Removing_a_scope_deletes_every_temp_file_in_it()
    {
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var reservation = Reserve(Registry);
            Registry.TryStartUpload(reservation.AttachmentId);
            Registry.Complete(reservation.AttachmentId, StoreFile(_environment, Kilobyte));
        }

        Registry.RemoveAll(Owner, _scopeKey);

        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public void A_change_announces_the_scope_it_belongs_to()
    {
        var reservation = Reserve(Registry);
        var announced = new List<Guid>();
        Registry.Changed += announced.Add;

        Registry.TryStartUpload(reservation.AttachmentId);

        Assert.Equal([_scopeKey], announced);
    }

    [Fact]
    public void The_sweep_removes_attachments_older_than_the_cutoff_and_keeps_newer_ones()
    {
        var time = new ManualTime();
        using var environment = new AttachmentTestEnvironment(time: time);
        var old = Reserve(environment.Registry);
        time.Advance(TimeSpan.FromHours(2));
        var recent = Reserve(environment.Registry);

        environment.Registry.RemoveOlderThan(time.GetUtcNow() - TimeSpan.FromHours(1));

        Assert.Equal([recent.AttachmentId], environment.Registry.List(Owner, _scopeKey).Select(listed => listed.AttachmentId));
    }

    [Fact]
    public void The_same_content_completed_twice_in_one_scope_is_refused_the_second_time_and_its_temp_file_deleted()
    {
        var sha256 = UploadHashes.Unique();
        CompleteUpload(_environment, _scopeKey, sha256);
        var second = Reserve(Registry);
        Registry.TryStartUpload(second.AttachmentId);
        var stored = StoreFile(_environment, Kilobyte, sha256);

        Registry.Complete(second.AttachmentId, stored);

        var listed = Registry.List(Owner, _scopeKey).Single(attachment => attachment.AttachmentId == second.AttachmentId);
        Assert.Equal((AttachmentStatus.Rejected, UploadRejections.AlreadyPending), (listed.Status, listed.RejectionReason));
        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void The_same_content_in_two_scopes_is_kept_in_both()
    {
        var sha256 = UploadHashes.Unique();
        var otherScope = Guid.NewGuid();
        CompleteUpload(_environment, _scopeKey, sha256);

        CompleteUpload(_environment, otherScope, sha256);

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(Registry.List(Owner, otherScope)).Status);
    }

    [Fact]
    public void Content_whose_earlier_copy_was_removed_is_kept()
    {
        var sha256 = UploadHashes.Unique();
        CompleteUpload(_environment, _scopeKey, sha256);
        Registry.Remove(Owner, Assert.Single(Registry.List(Owner, _scopeKey)).AttachmentId);

        CompleteUpload(_environment, _scopeKey, sha256);

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(Registry.List(Owner, _scopeKey)).Status);
    }

    // The more specific reason wins: the user can act on "already chosen".
    [Fact]
    public void A_duplicate_that_is_also_over_the_pending_bytes_limit_is_refused_as_a_duplicate()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxFileBytes = 10 * Kilobyte, MaxPendingBytesPerUser = 15 * Kilobyte });
        var sha256 = UploadHashes.Unique();
        CompleteUpload(environment, _scopeKey, sha256, size: 10 * Kilobyte);
        var second = Reserve(environment.Registry, size: 1);
        environment.Registry.TryStartUpload(second.AttachmentId);

        environment.Registry.Complete(second.AttachmentId, StoreFile(environment, 10 * Kilobyte, sha256));

        Assert.Equal(UploadRejections.AlreadyPending, environment.Registry.List(Owner, _scopeKey).Single(attachment => attachment.AttachmentId == second.AttachmentId).RejectionReason);
    }
}
