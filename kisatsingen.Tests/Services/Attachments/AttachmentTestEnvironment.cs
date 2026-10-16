using kisatsingen.Services;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Tests.Services.Attachments;

// The real upload stack over a parent folder of its own, deleted afterwards,
// so tests can assert on the files actually left on disk and never touch the
// app's folder.
internal sealed class AttachmentTestEnvironment : IDisposable
{
    public AttachmentTestEnvironment(AttachmentOptions? options = null)
    {
        Options = options ?? new AttachmentOptions();
        ParentFolder = Path.Combine(Path.GetTempPath(), "kisatsingen-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ParentFolder);
        Storage = AttachmentStorage.StartFresh(ParentFolder, NullLogger<AttachmentStorage>.Instance);
        Quota = new AttachmentQuota(Storage, Options);
        Uploader = new AttachmentUploader(Storage);
    }

    public string ParentFolder { get; }
    public string Folder => Storage.Folder;
    public AttachmentOptions Options { get; }
    public AttachmentStorage Storage { get; }
    public AttachmentQuota Quota { get; }
    public AttachmentUploader Uploader { get; }

    public RecordingMetricsService Metrics { get; } = new();

    public IReadOnlyList<string> FilesOnDisk => Directory.GetFiles(Folder);

    public ChatAttachments CreateChatAttachments(IAuthenticationService authentication, IMetricsService? metrics = null) =>
        new(
            authentication,
            Uploader,
            Quota,
            Options,
            new AttachmentMetrics(metrics ?? Metrics, NullLogger<AttachmentMetrics>.Instance),
            NullLogger<ChatAttachments>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(ParentFolder))
        {
            Directory.Delete(ParentFolder, recursive: true);
        }
    }
}
