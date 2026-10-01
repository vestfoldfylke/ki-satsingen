using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class PendingAttachmentRegistryTakeReadyTests : IDisposable
{
    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly Guid _scopeKey = Guid.NewGuid();

    public void Dispose() => _environment.Dispose();

    private PendingAttachmentRegistry Registry => _environment.Registry;

    private TempFile AttachReady(string owner = Owner, string fileName = "notat.md")
    {
        var reservation = Registry.Reserve(owner, _scopeKey, fileName, 10) ?? throw new InvalidOperationException("Reservation refused.");
        Registry.TryStartUpload(reservation.UploadId);
        var (file, stream) = _environment.Store.Create();
        stream.Dispose();
        Registry.Complete(reservation.UploadId, new UploadOutcome.Stored(file, 10, new string('a', 64)));
        return file;
    }

    private IReadOnlySet<Guid> AllInScope() => Registry.List(Owner, _scopeKey).Select(attachment => attachment.UploadId).ToHashSet();

    [Fact]
    public void An_attachment_not_among_the_requested_upload_ids_is_not_taken()
    {
        AttachReady(fileName: "sendt.md");
        var requested = AllInScope();
        AttachReady(fileName: "senere.md");

        var taken = Registry.TakeReady(Owner, _scopeKey, requested);

        Assert.Equal(["sendt.md"], taken.Select(attachment => attachment.FileName));
        Assert.Equal(["senere.md"], Registry.List(Owner, _scopeKey).Select(attachment => attachment.FileName));
    }

    [Fact]
    public void Taking_ready_hands_over_every_ready_attachment_with_its_type()
    {
        AttachReady(fileName: "a.md");
        AttachReady(fileName: "b.png");

        var taken = Registry.TakeReady(Owner, _scopeKey, AllInScope());

        Assert.Equal([("a.md", "text/markdown"), ("b.png", "image/png")], taken.Select(attachment => (attachment.FileName, attachment.Type.ContentType)));
    }

    [Fact]
    public void A_taken_attachment_leaves_the_composer()
    {
        AttachReady();

        Registry.TakeReady(Owner, _scopeKey, AllInScope());

        Assert.Empty(Registry.List(Owner, _scopeKey));
    }

    // The whole point of taking it: nothing in the registry can delete it now.
    [Fact]
    public void A_taken_temp_file_survives_the_composer_being_cleared()
    {
        var file = AttachReady();
        Registry.TakeReady(Owner, _scopeKey, AllInScope());

        Registry.RemoveAll(Owner, _scopeKey);

        Assert.True(File.Exists(file.Path));
    }

    // Pending for most of a day, then sent: the age sweep must not reach it
    // while it is being processed.
    [Fact]
    public void A_taken_file_has_its_age_reset_so_the_age_sweep_leaves_it_alone()
    {
        var file = AttachReady();
        File.SetLastWriteTimeUtc(file.Path, DateTime.UtcNow.AddDays(-2));
        Registry.TakeReady(Owner, _scopeKey, AllInScope());

        _environment.Store.DeleteOlderThan(DateTimeOffset.UtcNow.AddDays(-1));

        Assert.True(File.Exists(file.Path));
    }

    [Fact]
    public void Refused_and_unfinished_attachments_are_not_taken()
    {
        Registry.Reserve(Owner, _scopeKey, "program.exe", 10);
        Registry.Reserve(Owner, _scopeKey, "venter.md", 10);

        var taken = Registry.TakeReady(Owner, _scopeKey, AllInScope());

        Assert.Empty(taken);
        Assert.Equal(2, Registry.List(Owner, _scopeKey).Count);
    }

    [Fact]
    public void Another_owner_using_the_same_scope_key_takes_nothing()
    {
        AttachReady();

        var taken = Registry.TakeReady(OtherOwner, _scopeKey, AllInScope());

        Assert.Empty(taken);
    }
}
