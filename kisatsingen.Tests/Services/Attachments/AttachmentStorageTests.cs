using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

// File modes and links are tested in AttachmentStorageUnixTests.
public sealed class AttachmentStorageTests : IDisposable
{
    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";

    private TempFile CreateFile(string owner = Owner, long size = 0)
    {
        var (file, stream) = _environment.Storage.CreateFile(owner, Guid.NewGuid());
        using (stream)
        {
            stream.SetLength(size);
        }

        return file;
    }

    // What the app does on its next start.
    private AttachmentStorage StartAgain() =>
        AttachmentStorage.StartFresh(_environment.ParentFolder, NullLogger<AttachmentStorage>.Instance);

    [Fact]
    public void A_created_file_lives_in_the_storage_folder_under_a_name_of_its_own()
    {
        var first = CreateFile();
        var second = CreateFile();

        Assert.Equal(_environment.Folder, Path.GetDirectoryName(first.Path));
        Assert.NotEqual(first.Path, second.Path);
    }

    [Fact]
    public void A_new_file_is_named_as_uploading_after_its_attachment_and_never_shows_the_owner_id()
    {
        var attachmentId = Guid.NewGuid();
        var (file, stream) = _environment.Storage.CreateFile("user-object-id", attachmentId);
        stream.Dispose();

        var fileName = Path.GetFileName(file.Path);

        Assert.StartsWith("uploading-", fileName);
        Assert.EndsWith(attachmentId.ToString("N"), fileName);
        Assert.DoesNotContain("user-object-id", fileName);
    }

    [Fact]
    public void Marking_a_file_ready_renames_it_and_the_handle_follows()
    {
        var file = CreateFile(Owner, size: 100);
        var uploadingPath = file.Path;

        var isMarked = _environment.Storage.TryMarkReady(file);

        Assert.True(isMarked);
        Assert.StartsWith("ready-", Path.GetFileName(file.Path));
        Assert.True(File.Exists(file.Path));
        Assert.False(File.Exists(uploadingPath));
    }

    [Fact]
    public void An_owners_usage_counts_only_their_own_files_uploading_and_ready()
    {
        CreateFile(Owner, size: 100);
        _environment.Storage.TryMarkReady(CreateFile(Owner, size: 50));
        CreateFile(OtherOwner, size: 1000);

        var usage = _environment.Storage.UsageOf(Owner);

        Assert.Equal(new StorageUsage(2, 150), usage);
    }

    [Fact]
    public void An_owners_ready_usage_leaves_out_uploads_still_in_progress()
    {
        CreateFile(Owner, size: 100);
        _environment.Storage.TryMarkReady(CreateFile(Owner, size: 50));

        var usage = _environment.Storage.ReadyUsageOf(Owner);

        Assert.Equal(new StorageUsage(1, 50), usage);
    }

    [Fact]
    public void Total_usage_counts_every_owners_files()
    {
        CreateFile(Owner, size: 100);
        CreateFile(OtherOwner, size: 1000);

        var usage = _environment.Storage.TotalUsage();

        Assert.Equal(new StorageUsage(2, 1100), usage);
    }

    [Fact]
    public void Total_ready_usage_leaves_out_uploads_still_in_progress()
    {
        CreateFile(Owner, size: 100);
        _environment.Storage.TryMarkReady(CreateFile(OtherOwner, size: 1000));

        var usage = _environment.Storage.TotalReadyUsage();

        Assert.Equal(new StorageUsage(1, 1000), usage);
    }

    [Fact]
    public void A_deleted_file_no_longer_counts()
    {
        var file = CreateFile(Owner, size: 100);

        file.Dispose();

        Assert.Equal(new StorageUsage(0, 0), _environment.Storage.UsageOf(Owner));
    }

    [Fact]
    public void Starting_fresh_wipes_everything_an_earlier_run_left_in_the_folder()
    {
        CreateFile();
        File.WriteAllText(Path.Combine(_environment.Folder, "not-made-by-the-storage.txt"), "left behind");
        Directory.CreateDirectory(Path.Combine(_environment.Folder, "a-folder"));

        var restarted = StartAgain();

        Assert.Empty(Directory.EnumerateFileSystemEntries(restarted.Folder));
    }

    [Fact]
    public void Starting_fresh_leaves_everything_else_in_the_parent_folder_alone()
    {
        var neighbour = Path.Combine(_environment.ParentFolder, "someone-elses.txt");
        File.WriteAllText(neighbour, "keep me");

        StartAgain();

        Assert.True(File.Exists(neighbour));
    }
}
