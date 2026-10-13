namespace kisatsingen.Services.Attachments;

// The turn owns these files from send. Disposing releases whichever are
// still on disk.
internal sealed class SentAttachments(IReadOnlyList<ReadyAttachment> attachments) : IDisposable
{
    public IReadOnlyList<ReadyAttachment> All => attachments;

    public IReadOnlyList<string> FileNames => [.. attachments.Select(attachment => attachment.FileName)];

    public void Dispose()
    {
        foreach (var attachment in attachments)
        {
            attachment.File.Dispose();
        }
    }
}
