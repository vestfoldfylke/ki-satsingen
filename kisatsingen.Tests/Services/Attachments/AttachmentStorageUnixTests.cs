using System.Runtime.Versioning;
using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

// File modes and links: Windows has no modes, and creating a link there needs
// Developer Mode or an administrator.
[UnsupportedOSPlatform("windows")]
public sealed class AttachmentStorageUnixTests : IDisposable
{
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    // What the app does on its next start.
    private AttachmentStorage StartAgain() =>
        AttachmentStorage.StartFresh(_environment.ParentFolder, NullLogger<AttachmentStorage>.Instance);

    [UnixOnlyFact]
    public void A_created_file_is_readable_and_writable_by_its_owner_only()
    {
        var (file, stream) = _environment.Storage.CreateFile("owner", Guid.NewGuid());
        stream.Dispose();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.Path));
    }

    [UnixOnlyFact]
    public void The_folder_is_usable_by_its_owner_only()
    {
        Assert.Equal(OwnerOnlyFolder, File.GetUnixFileMode(_environment.Folder));
    }

    [UnixOnlyFact]
    public void Starting_fresh_makes_a_widened_folder_owner_only_again()
    {
        File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var restarted = StartAgain();

        Assert.Equal(OwnerOnlyFolder, File.GetUnixFileMode(restarted.Folder));
    }

    // Uploads must never land wherever a planted link points, and wiping must
    // never reach into its target.
    [UnixOnlyFact]
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
    [UnixOnlyFact]
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
    [UnixOnlyFact]
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
    [UnixOnlyFact]
    public void A_folder_that_is_not_owner_only_is_refused()
    {
        File.SetUnixFileMode(_environment.Folder, OwnerOnlyFolder | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Assert.Throws<InvalidOperationException>(() => AttachmentStorage.EnsureOwnerOnly(_environment.Folder));
    }

    // Stands in for an owner-only folder of another user, which a test cannot
    // create without root.
    [UnixOnlyFact]
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

    [UnixOnlyFact]
    public void A_link_in_the_folders_place_is_refused()
    {
        var target = Directory.CreateDirectory(Path.Combine(_environment.ParentFolder, "target")).FullName;
        Directory.Delete(_environment.Folder, recursive: true);
        Directory.CreateSymbolicLink(_environment.Folder, target);

        Assert.Throws<InvalidOperationException>(() => AttachmentStorage.EnsureOwnerOnly(_environment.Folder));
    }
}
