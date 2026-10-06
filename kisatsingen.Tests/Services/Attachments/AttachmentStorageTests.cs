using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentStorageTests : IDisposable
{
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    private TempFile CreateFile()
    {
        var (file, stream) = _environment.Storage.Create();
        stream.Dispose();
        return file;
    }

    // What the app does on its next start.
    private AttachmentStorage StartAgain() =>
        AttachmentStorage.StartFresh(_environment.ParentFolder, NullLogger<AttachmentStorage>.Instance);

    [Fact]
    public void A_created_file_lives_in_the_stores_folder_under_a_name_of_its_own()
    {
        var first = CreateFile();
        var second = CreateFile();

        Assert.Equal(_environment.Folder, Path.GetDirectoryName(first.Path));
        Assert.NotEqual(first.Path, second.Path);
    }

    [Fact]
    public void A_created_file_is_readable_and_writable_by_its_owner_only()
    {
        var file = CreateFile();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.Path));
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
