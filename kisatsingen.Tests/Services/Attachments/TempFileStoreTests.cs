using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class TempFileStoreTests : IDisposable
{
    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    private TempFile CreateFile()
    {
        var (file, stream) = _environment.Store.Create();
        stream.Dispose();
        return file;
    }

    [Fact]
    public void A_created_file_lives_in_the_stores_folder_under_a_name_of_its_own()
    {
        var first = CreateFile();
        var second = CreateFile();

        Assert.Equal(_environment.Directory, Path.GetDirectoryName(first.Path));
        Assert.NotEqual(first.Path, second.Path);
    }

    // Returns early on Windows, which has no Unix file modes; xunit 2 has no
    // runtime skip.
    [Fact]
    public void A_created_file_is_readable_and_writable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = CreateFile();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file.Path));
    }

    [Fact]
    public void Deleting_everything_removes_every_file_the_store_created()
    {
        CreateFile();
        CreateFile();

        var deleted = _environment.Store.DeleteAll();

        Assert.Equal(2, deleted);
        Assert.Empty(_environment.FilesOnDisk);
    }

    // Cleanup must never reach a file that is not ours, wherever it came from.
    [Fact]
    public void Deleting_everything_leaves_files_the_store_did_not_create()
    {
        var foreign = Path.Combine(_environment.Directory, "not-ours.txt");
        File.WriteAllText(foreign, "keep me");
        CreateFile();

        _environment.Store.DeleteAll();

        Assert.Equal([foreign], _environment.FilesOnDisk);
    }

    [Fact]
    public void Deleting_by_age_keeps_files_newer_than_the_cutoff()
    {
        var old = CreateFile();
        File.SetLastWriteTimeUtc(old.Path, DateTime.UtcNow.AddDays(-2));
        var recent = CreateFile();

        _environment.Store.DeleteOlderThan(DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Equal([recent.Path], _environment.FilesOnDisk);
    }

    // Uploads would otherwise land wherever the link points. Returns early on
    // Windows, where creating a directory link needs extra rights.
    [Fact]
    public void A_folder_that_is_a_link_is_refused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = Directory.CreateDirectory(Path.Combine(_environment.Directory, "target")).FullName;
        var link = Path.Combine(_environment.Directory, "link");
        Directory.CreateSymbolicLink(link, target);

        Assert.Throws<InvalidOperationException>(() => new TempFileStore(link, NullLogger<TempFileStore>.Instance));
    }
}
