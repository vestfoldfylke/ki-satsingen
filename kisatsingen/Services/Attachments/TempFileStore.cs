using System.Runtime.Versioning;

namespace kisatsingen.Services.Attachments;

// A file this store created. Not constructible outside it, so Delete can only
// ever be handed a path inside the store's own folder.
public sealed class TempFile
{
    internal TempFile(string path) => Path = path;

    public string Path { get; }
}

// Uploaded bytes between upload and processing, in one folder nothing else
// writes to. Names are random, so a client-supplied name never reaches the
// file system; and files are owner-only, so another local account cannot read
// what users uploaded.
//
// The folder is fixed rather than configurable: the default is local to the
// instance and outside /home, so outside App Service's shared storage and
// backups, and a configured one could point anywhere.
public sealed class TempFileStore
{
    public const string DefaultFolderName = "kisatsingen-attachments";

    // Every file the store creates carries it, and cleanup deletes nothing
    // else, so a file that is not ours survives even if it lands in the folder.
    private const string FilePrefix = "att-";

    private static readonly EnumerationOptions OwnFilesOnly = new()
    {
        MatchCasing = MatchCasing.CaseSensitive,
        RecurseSubdirectories = false
    };

    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly ILogger<TempFileStore> _logger;

    public TempFileStore(string directory, ILogger<TempFileStore> logger)
    {
        _logger = logger;
        Directory = directory;
        EnsureDirectory(directory);
    }

    public string Directory { get; }

    // CreateNew, so a name that somehow already exists fails instead of being
    // overwritten.
    public (TempFile File, FileStream Stream) Create()
    {
        var path = System.IO.Path.Combine(Directory, FilePrefix + System.IO.Path.GetRandomFileName());
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        return (new TempFile(path), new FileStream(path, options));
    }

    // Never throws: a file that cannot be deleted now is caught by the sweep,
    // and a caller cleaning up must not have its own outcome replaced.
    public void Delete(TempFile file)
    {
        try
        {
            File.Delete(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete temp file {Path}. The hourly sweep will retry.", file.Path);
        }
    }

    // On startup: whatever of ours is here belongs to a process that no longer
    // exists.
    public int DeleteAll() => DeleteWhere(_ => true);

    // Only the files nothing tracks any more; tracked ones are removed through
    // the registry, which also forgets them.
    public int DeleteOlderThan(DateTimeOffset cutoff) =>
        DeleteWhere(path => File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime);

    private int DeleteWhere(Func<string, bool> shouldDelete)
    {
        var deleted = 0;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, FilePrefix + "*", OwnFilesOnly))
        {
            if (!shouldDelete(path))
            {
                continue;
            }

            Delete(new TempFile(path));
            deleted++;
        }

        return deleted;
    }

    // A symlink in place of the folder would send uploads wherever it points.
    private static void EnsureDirectory(string directory)
    {
        var info = OperatingSystem.IsWindows()
            ? System.IO.Directory.CreateDirectory(directory)
            : System.IO.Directory.CreateDirectory(directory, OwnerOnlyFolder);

        if (info.LinkTarget is not null)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{directory}' is a link to '{info.LinkTarget}'. Delete the link before starting the app; it will be recreated as a real folder.");
        }

        // CreateDirectory leaves an existing folder's mode alone.
        if (!OperatingSystem.IsWindows())
        {
            RestrictToOwner(directory);
        }
    }

    // Only the folder's owner may change its mode, so this is where a folder
    // left by another user surfaces, for instance after the app starts running
    // as a different one.
    [UnsupportedOSPlatform("windows")]
    private static void RestrictToOwner(string directory)
    {
        try
        {
            File.SetUnixFileMode(directory, OwnerOnlyFolder);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{directory}' belongs to another user, so this process cannot restrict or use it. " +
                "It only ever holds temporary uploads: delete the folder and restart the app, and it will be recreated for the current user.",
                ex);
        }
    }
}
