using kisatsingen.Services;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;

namespace kisatsingen.Tests.Services.Attachments;

// The real upload stack over a parent folder of its own, deleted afterwards,
// so tests can assert on the files actually left on disk and never touch the
// app's folder.
internal sealed class AttachmentTestEnvironment : IDisposable
{
    public AttachmentTestEnvironment(AttachmentOptions? options = null, TimeProvider? time = null)
    {
        Options = options ?? new AttachmentOptions();
        ParentFolder = Path.Combine(Path.GetTempPath(), "kisatsingen-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(ParentFolder);
        Store = TempFileStore.StartFresh(ParentFolder, NullLogger<TempFileStore>.Instance);
        Registry = new PendingAttachmentRegistry(Store, Options, time ?? TimeProvider.System);
        Uploader = new AttachmentUploader(Store);
    }

    public string ParentFolder { get; }
    public string Directory => Store.Directory;
    public AttachmentOptions Options { get; }
    public TempFileStore Store { get; }
    public PendingAttachmentRegistry Registry { get; }
    public AttachmentUploader Uploader { get; }

    public IReadOnlyList<string> FilesOnDisk => System.IO.Directory.GetFiles(Directory);

    public ChatAttachments CreateChatAttachments(IAuthenticationService authentication) =>
        new(authentication, Registry, Uploader, Options, NullLogger<ChatAttachments>.Instance);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(ParentFolder))
        {
            System.IO.Directory.Delete(ParentFolder, recursive: true);
        }
    }
}
