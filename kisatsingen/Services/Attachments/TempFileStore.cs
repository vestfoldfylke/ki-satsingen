namespace kisatsingen.Services.Attachments;

// A file this store created. Not constructible outside it, so Delete can only
// ever be handed a path inside the store's own folder.
public sealed class TempFile
{
    internal TempFile(string path) => Path = path;

    public string Path { get; }
}

// Uploaded bytes between upload and processing, in one folder that is wiped
// and recreated at every start. Names are random, so a client-supplied name
// never reaches the file system; and the folder and files are owner-only, so
// another local account cannot read what users uploaded.
//
// The folder is fixed rather than configurable, so it cannot be misconfigured,
// and it lives in the temp folder: local to the instance and outside /home, so
// outside App Service's shared storage and backups.
public sealed class TempFileStore
{
    private const string FolderName = "kisatsingen-attachments";

    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // What FileStream uses when given none; named because the constructor
    // that takes FileOptions requires a size.
    private const int FileStreamDefaultBufferSize = 4096;

    private readonly ILogger<TempFileStore> _logger;

    private TempFileStore(string directory, ILogger<TempFileStore> logger)
    {
        Directory = directory;
        _logger = logger;
    }

    public string Directory { get; }

    // Wipes whatever an earlier run left, then creates the folder anew, so it
    // is this process's own and owner-only. A link planted in the folder's
    // place is unlinked, never followed into its target.
    //
    // Assumes one app process per temp folder, and no hostile user sharing it.
    // Both hold because every App Service container has its own /tmp, and on
    // macOS the temp folder is per user. A second process sharing it would
    // silently wipe the first one's pending uploads. On a Linux /tmp shared
    // with a hostile user, the recursive wipe could be raced into deleting the
    // running user's own files.
    public static TempFileStore StartFresh(string parentFolder, ILogger<TempFileStore> logger)
    {
        var directory = Path.Combine(parentFolder, FolderName);

        var leftovers = Wipe(directory);
        System.IO.Directory.CreateDirectory(directory, OwnerOnlyFolder);
        EnsureOwnerOnly(directory);
        EnsureWritable(directory);

        logger.LogInformation("Started the attachment temp folder {Directory}, wiping {Leftovers} entries left by an earlier run.", directory, leftovers);
        return new TempFileStore(directory, logger);
    }

    // CreateNew, so a name that somehow already exists fails instead of being
    // overwritten.
    public (TempFile File, FileStream Stream) Create()
    {
        var path = Path.Combine(Directory, Path.GetRandomFileName());
        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = OwnerOnlyFile
        });

        _logger.LogDebug("Created temp file {FileName}.", Path.GetFileName(path));
        return (new TempFile(path), stream);
    }

    public FileStream OpenRead(TempFile file) =>
        new(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileStreamDefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    // Never throws: a file that cannot be deleted now is wiped at the next
    // start, and a caller cleaning up must not have its own outcome replaced.
    public void Delete(TempFile file)
    {
        var fileName = Path.GetFileName(file.Path);
        try
        {
            File.Delete(file.Path);
            _logger.LogDebug("Deleted temp file {FileName}.", fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete temp file {FileName}. It is wiped at the next start.", fileName);
        }
    }

    // A link is not counted: its target's entries are not ours, and the wipe
    // only removes the link.
    private static int CountLeftovers(string directory)
    {
        var info = new DirectoryInfo(directory);
        return info.Exists && info.LinkTarget is null ? info.EnumerateFileSystemInfos().Count() : 0;
    }

    // Starting on top of old files would break the promise that no upload
    // outlives a restart, so a failed wipe stops startup. Counting is inside
    // the try too: an unreadable folder must get the same advice.
    private static int Wipe(string directory)
    {
        try
        {
            var leftovers = CountLeftovers(directory);
            System.IO.Directory.Delete(directory, recursive: true);
            return leftovers;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not wipe the attachment temp folder '{directory}' at startup: {ex.Message} " +
                "It only ever holds temporary uploads: delete it by hand and restart the app, and it will be recreated empty.",
                ex);
        }
    }

    // CreateDirectory succeeds without applying the mode when the path already
    // exists, so a folder or link planted between the wipe and the create
    // would otherwise be used as is.
    internal static void EnsureOwnerOnly(string directory)
    {
        var created = new DirectoryInfo(directory);
        if (created.LinkTarget is not null || created.UnixFileMode != OwnerOnlyFolder)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{directory}' was replaced while the app started, or is not owner-only. " +
                "It only ever holds temporary uploads: delete it by hand and restart the app, and it will be recreated empty.");
        }
    }

    // An owner-only folder planted by another user passes EnsureOwnerOnly but
    // cannot be written to. It leaks nothing, but every upload would fail, so
    // a probe file finds it at startup instead.
    internal static void EnsureWritable(string directory)
    {
        var probe = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            File.Create(probe).Dispose();
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{directory}' cannot be written to: {ex.Message} " +
                "It may belong to another user. Delete it by hand and restart the app, and it will be recreated for this one.",
                ex);
        }
    }
}
