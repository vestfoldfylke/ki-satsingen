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

    private static UploadOutcome.Stored StoreFile(AttachmentTestEnvironment environment, long size)
    {
        var (file, stream) = environment.Store.Create();
        stream.Dispose();
        return new UploadOutcome.Stored(file, size, new string('a', 64));
    }

    [Fact]
    public void A_file_with_an_extension_that_is_not_allowed_is_listed_as_refused_with_the_reason()
    {
        var reservation = Registry.Reserve(Owner, _scopeKey, "program.exe", Kilobyte);

        Assert.Null(reservation);
        var listed = Assert.Single(Registry.List(Owner, _scopeKey));
        Assert.Equal((AttachmentStatus.Rejected, UploadRejections.UnsupportedType), (listed.Status, listed.RejectionReason));
    }

    [Fact]
    public void A_file_declared_over_the_size_limit_is_refused_before_any_bytes_are_read()
    {
        var reservation = Registry.Reserve(Owner, _scopeKey, "notat.md", _environment.Options.MaxFileBytes + 1);

        Assert.Null(reservation);
    }

    [Fact]
    public void The_pending_file_limit_counts_one_owners_files_across_all_their_scopes()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        Reserve(environment.Registry, scopeKey: Guid.NewGuid());

        var reservation = environment.Registry.Reserve(Owner, _scopeKey, "second.md", Kilobyte);

        Assert.Null(reservation);
    }

    [Fact]
    public void Refused_files_do_not_count_towards_the_pending_file_limit()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        environment.Registry.Reserve(Owner, _scopeKey, "program.exe", Kilobyte);

        var reservation = environment.Registry.Reserve(Owner, _scopeKey, "notat.md", Kilobyte);

        Assert.NotNull(reservation);
    }

    [Fact]
    public void A_file_that_would_take_the_owner_over_the_pending_bytes_limit_is_refused()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxFileBytes = 10 * Kilobyte, MaxPendingBytesPerUser = 15 * Kilobyte });
        Reserve(environment.Registry, size: 10 * Kilobyte);

        var reservation = environment.Registry.Reserve(Owner, _scopeKey, "second.md", 10 * Kilobyte);

        Assert.Null(reservation);
    }

    // The reservation check trusts the declared size; completion checks the real one.
    [Fact]
    public void A_file_larger_than_it_declared_is_refused_on_completion_and_deleted()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxFileBytes = 10 * Kilobyte, MaxPendingBytesPerUser = 15 * Kilobyte });
        var first = Reserve(environment.Registry, size: 10 * Kilobyte);
        environment.Registry.TryStartUpload(first.UploadId);
        environment.Registry.Complete(first.UploadId, StoreFile(environment, 10 * Kilobyte));
        var liar = Reserve(environment.Registry, size: 1);
        environment.Registry.TryStartUpload(liar.UploadId);
        var stored = StoreFile(environment, 10 * Kilobyte);

        environment.Registry.Complete(liar.UploadId, stored);

        Assert.Equal(AttachmentStatus.Rejected, environment.Registry.List(Owner, _scopeKey).Single(listed => listed.UploadId == liar.UploadId).Status);
        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void An_upload_over_the_concurrent_limit_is_refused_when_it_would_start()
    {
        using var environment = WithOptions(new AttachmentOptions { MaxConcurrentUploadsPerUser = 1 });
        var first = Reserve(environment.Registry);
        var second = Reserve(environment.Registry, scopeKey: Guid.NewGuid());
        environment.Registry.TryStartUpload(first.UploadId);

        var isStarted = environment.Registry.TryStartUpload(second.UploadId);

        Assert.False(isStarted);
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

        var isRemoved = Registry.Remove(OtherOwner, reservation.UploadId);

        Assert.False(isRemoved);
    }

    [Fact]
    public void Removing_an_attachment_cancels_its_upload()
    {
        var reservation = Reserve(Registry);

        Registry.Remove(Owner, reservation.UploadId);

        Assert.True(reservation.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Removing_a_ready_attachment_deletes_its_temp_file()
    {
        var reservation = Reserve(Registry);
        Registry.TryStartUpload(reservation.UploadId);
        var stored = StoreFile(_environment, Kilobyte);
        Registry.Complete(reservation.UploadId, stored);

        Registry.Remove(Owner, reservation.UploadId);

        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void A_file_completed_after_its_attachment_was_removed_is_deleted()
    {
        var reservation = Reserve(Registry);
        Registry.TryStartUpload(reservation.UploadId);
        Registry.Remove(Owner, reservation.UploadId);
        var stored = StoreFile(_environment, Kilobyte);

        Registry.Complete(reservation.UploadId, stored);

        Assert.False(File.Exists(stored.File.Path));
    }

    [Fact]
    public void Removing_a_scope_deletes_every_temp_file_in_it()
    {
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var reservation = Reserve(Registry);
            Registry.TryStartUpload(reservation.UploadId);
            Registry.Complete(reservation.UploadId, StoreFile(_environment, Kilobyte));
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

        Registry.TryStartUpload(reservation.UploadId);

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

        Assert.Equal([recent.UploadId], environment.Registry.List(Owner, _scopeKey).Select(listed => listed.UploadId));
    }
}
