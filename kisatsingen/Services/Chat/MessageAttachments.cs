using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.Chat;

// The attachments that belong to one message: those ready when it was sent.
// Their IDs are captured then, so one that becomes ready later is not taken
// with them.
internal sealed class MessageAttachments(ChatAttachments composer, IReadOnlySet<Guid> attachmentIds)
{
    // The caller now owns their temp files (see ReadyAttachment).
    public IReadOnlyList<ReadyAttachment> TakeReady() => composer.TakeReady(attachmentIds);
}
