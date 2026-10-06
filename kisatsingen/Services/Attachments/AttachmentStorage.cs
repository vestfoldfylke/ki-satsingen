namespace kisatsingen.Services.Attachments;

// Created only by AttachmentStorage, so its Delete never reaches a path
// outside the attachment folder.
public sealed class TempFile
{
    internal TempFile(string path) => Path = path;

    public string Path { get; }
}

// Uploaded bytes between upload and processing. Names are random, so a
// client-supplied name never reaches the file system; and the folder and
// files are owner-only, so another local account cannot read what users
// uploaded.
//
// The folder is fixed rather than configurable, so it cannot be misconfigured,
// and it lives in the temp folder: local to the instance and outside /home, so
// outside App Service's shared storage and backups.
public sealed class AttachmentStorage
{
    private const string FolderName = "kisatsingen-attachments";

    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // Ends every startup error about the folder. "Delete it by hand" alone is
    // no help on App Service behind a VNet, and there the cause is almost
    // never the folder itself.
    private const string FolderProblemAdvice =
        "The folder only holds temporary uploads, so deleting it loses nothing. " +
        "Locally: delete it (with sudo if root owns it, for example after running the app with sudo) and start the app again. " +
        "On App Service, where only this app uses /tmp, this points to a failing disk or to an image that now runs the app as a different user: " +
        "check the instance's health and the latest image or deployment change. For HARD RESET - scale App Service Plan up or down.";

    private readonly ILogger<AttachmentStorage> _logger;

    private AttachmentStorage(string folder, ILogger<AttachmentStorage> logger)
    {
        Folder = folder;
        _logger = logger;
    }

    public string Folder { get; }

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
    public static AttachmentStorage StartFresh(string parentFolder, ILogger<AttachmentStorage> logger)
    {
        var folder = Path.Combine(parentFolder, FolderName);

        var leftoverCount = WipeLeftovers(folder);
        Directory.CreateDirectory(folder, OwnerOnlyFolder);
        EnsureOwnerOnly(folder);
        EnsureWritable(folder);

        logger.LogInformation("Started the attachment temp folder {Folder}, wiping {LeftoverCount} entries left by an earlier run.", folder, leftoverCount);
        return new AttachmentStorage(folder, logger);
    }

    // CreateNew, so a name that somehow already exists fails instead of being
    // overwritten.
    public (TempFile File, FileStream Stream) Create()
    {
        var path = Path.Combine(Folder, Path.GetRandomFileName());
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
        new(file.Path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

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
            _logger.LogWarning(ex, "Could not delete temp file {FileName}. It is wiped at the next StartFresh.", fileName);
        }
    }

    // Returns how many entries it wiped. Starting on top of old files would
    // break the promise that no upload outlives a restart, so a failed wipe
    // stops startup. Counting is inside the try too: an unreadable folder must
    // get the same advice.
    private static int WipeLeftovers(string folder)
    {
        try
        {
            var existing = new DirectoryInfo(folder);
            var isOurOwnFolder = existing.Exists && existing.LinkTarget is null;
            var leftoverCount = isOurOwnFolder ? existing.EnumerateFileSystemInfos().Count() : 0;

            Directory.Delete(folder, recursive: true);
            return leftoverCount;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not wipe the attachment temp folder '{folder}' at startup: {ex.Message} {FolderProblemAdvice}",
                ex);
        }
    }

    // CreateDirectory succeeds without applying the mode when the path already
    // exists, so a folder or link planted between the wipe and the create
    // would otherwise be used as is.
    internal static void EnsureOwnerOnly(string folder)
    {
        var created = new DirectoryInfo(folder);
        if (created.LinkTarget is not null || created.UnixFileMode != OwnerOnlyFolder)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{folder}' is a link or is not owner-only right after the app created it, " +
                $"so another process or user changed it. {FolderProblemAdvice}");
        }
    }

    // An owner-only folder that belongs to another user passes EnsureOwnerOnly,
    // so writing a test file checks that this process can actually write to it.
    internal static void EnsureWritable(string folder)
    {
        var writeTestFilePath = Path.Combine(folder, Path.GetRandomFileName());
        try
        {
            File.Create(writeTestFilePath).Dispose();
            File.Delete(writeTestFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The attachment temp folder '{folder}' cannot be written to, so it likely belongs to another user: {ex.Message} {FolderProblemAdvice}",
                ex);
        }
    }
}
