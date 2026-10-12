using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentStorageTests : IDisposable
{
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    private const string Owner = "owner";
    private const string OtherOwner = "someone-else";

    private TempFile CreateFile(string owner = Owner, long size = 0)
    {
        var (file, stream) = _environment.Storage.Create(owner, Guid.NewGuid());
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
        var (file, stream) = _environment.Storage.Create("user-object-id", attachmentId);
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
    public void A_created_file_is_readable_and_writable_by_its_owner_only()
    {
        var file = CreateFile();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.Path));
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

        _environment.Storage.Delete(file);

        Assert.Equal(new StorageUsage(0, 0), _environment.Storage.UsageOf(Owner));
    }

    [Fact]
    public void The_folder_is_usable_by_its_owner_only()
    {
        Assert.Equal(OwnerOnlyFolder, File.GetUnixFileMode(_environment.Folder));
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

    [Fact]
    public void Starting_fresh_makes_a_widened_folder_owner_only_again()
    {
        File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var restarted = StartAgain();

        Assert.Equal(OwnerOnlyFolder, File.GetUnixFileMode(restarted.Folder));
    }

    // Uploads must never land wherever a planted link points, and wiping must
    // never reach into its target.
    [Fact]
    public void Starting_fresh_replaces_a_link_in_the_folders_place_and_leaves_its_target_alone()
    {
        var target = Directory.CreateDirectory(Path.Combine(_environment.ParentFolder, "target")).FullName;
        var targetFile = Path.Combine(target, "precious.txt");
        File.WriteAllText(targetFile, "keep me");
        Directory.Delete(_environment.Folder, recursive: true);
        Directory.CreateSymbolicLink(_environment.Folder, target);

        var restarted = StartAgain();

        Assert.Null(new DirectoryInfo(restarted.Folder).LinkTarget);
        Assert.True(File.Exists(targetFile));
    }

    // A read-only subfolder makes the wipe fail for a normal user. Run as root,
    // the wipe succeeds and this test fails.
    [Fact]
    public void Starting_fresh_stops_with_an_error_when_the_folder_cannot_be_wiped()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_environment.Folder, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "stuck.txt"), "cannot be deleted");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Assert.Throws<InvalidOperationException>(StartAgain);
        }
        finally
        {
            File.SetUnixFileMode(locked, OwnerOnlyFolder);
        }
    }

    // Like a folder left by the app running as another user: it cannot even
    // be listed, and must still get the advice rather than a bare error.
    [Fact]
    public void Starting_fresh_stops_with_an_error_when_the_folder_cannot_be_read()
    {
        File.SetUnixFileMode(_environment.Folder, UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            Assert.Throws<InvalidOperationException>(StartAgain);
        }
        finally
        {
            File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder);
        }
    }

    // Stands in for a folder planted between the wipe and the create, a
    // moment a test cannot hit reliably.
    [Fact]
    public void A_folder_that_is_not_owner_only_is_refused()
    {
        File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Assert.Throws<InvalidOperationException>(() => AttachmentStorage.EnsureOwnerOnly(_environment.Folder));
    }

    // Stands in for an owner-only folder of another user, which a test cannot
    // create without root.
    [Fact]
    public void A_folder_that_cannot_be_written_to_is_refused()
    {
        File.SetUnixFileMode(_environment.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Assert.Throws<InvalidOperationException>(() => AttachmentStorage.EnsureWritable(_environment.Folder));
        }
        finally
        {
            File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder);
        }
    }

    [Fact]
    public void A_link_in_the_folders_place_is_refused()
    {
        var target = Directory.CreateDirectory(Path.Combine(_environment.ParentFolder, "target")).FullName;
        Directory.Delete(_environment.Folder, recursive: true);
        Directory.CreateSymbolicLink(_environment.Folder, target);

        Assert.Throws<InvalidOperationException>(() => AttachmentStorage.EnsureOwnerOnly(_environment.Folder));
    }
}
