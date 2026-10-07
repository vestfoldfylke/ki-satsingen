using kisatsingen.Services;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;

namespace kisatsingen.Tests.Services.Attachments;

// The real upload stack over a folder of its own, deleted afterwards, so tests
// can assert on the files actually left on disk.
internal sealed class AttachmentTestEnvironment : IDisposable
{
    public AttachmentTestEnvironment(AttachmentOptions? options = null, TimeProvider? time = null)
    {
        Options = options ?? new AttachmentOptions();
        Directory = Path.Combine(Path.GetTempPath(), "kisatsingen-tests", Guid.NewGuid().ToString("N"));
        Store = new TempFileStore(Directory, NullLogger<TempFileStore>.Instance);
        Registry = new PendingAttachmentRegistry(Store, Options, time ?? TimeProvider.System);
        Uploader = new AttachmentUploader(Store);
    }

    public string Directory { get; }
    public AttachmentOptions Options { get; }
    public TempFileStore Store { get; }
    public PendingAttachmentRegistry Registry { get; }
    public AttachmentUploader Uploader { get; }

    public IReadOnlyList<string> FilesOnDisk => System.IO.Directory.GetFiles(Directory);

    public ChatAttachments CreateChatAttachments(IAuthenticationService authentication) =>
        new(authentication, Registry, Uploader, Options, NullLogger<ChatAttachments>.Instance);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
