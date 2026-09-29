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

    [Fact]
    public void Taking_ready_hands_over_every_ready_attachment_with_its_type()
    {
        AttachReady(fileName: "a.md");
        AttachReady(fileName: "b.png");

        var taken = Registry.TakeReady(Owner, _scopeKey);

        Assert.Equal([("a.md", "text/markdown"), ("b.png", "image/png")], taken.Select(attachment => (attachment.FileName, attachment.Type.ContentType)));
    }

    [Fact]
    public void A_taken_attachment_leaves_the_composer()
    {
        AttachReady();

        Registry.TakeReady(Owner, _scopeKey);

        Assert.Empty(Registry.List(Owner, _scopeKey));
    }

    // The whole point of taking it: nothing in the registry can delete it now.
    [Fact]
    public void A_taken_temp_file_survives_the_composer_being_cleared()
    {
        var file = AttachReady();
        Registry.TakeReady(Owner, _scopeKey);

        Registry.RemoveAll(Owner, _scopeKey);

        Assert.True(File.Exists(file.Path));
    }

    [Fact]
    public void Refused_and_unfinished_attachments_are_not_taken()
    {
        Registry.Reserve(Owner, _scopeKey, "program.exe", 10);
        Registry.Reserve(Owner, _scopeKey, "venter.md", 10);

        var taken = Registry.TakeReady(Owner, _scopeKey);

        Assert.Empty(taken);
        Assert.Equal(2, Registry.List(Owner, _scopeKey).Count);
    }

    [Fact]
    public void Another_owner_using_the_same_scope_key_takes_nothing()
    {
        AttachReady();

        var taken = Registry.TakeReady(OtherOwner, _scopeKey);

        Assert.Empty(taken);
    }
}
