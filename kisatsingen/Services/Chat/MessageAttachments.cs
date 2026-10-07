using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.Chat;

// The attachments that belong to one message: those ready when it was sent.
// Owner, composer key and upload ids are captured then, so taking them later
// does not depend on what the composer holds by that time.
internal sealed class MessageAttachments(PendingAttachmentRegistry registry, string? ownerId, Guid composerKey, IReadOnlySet<Guid> uploadIds)
{
    // The caller now owns their temp files (see ReadyAttachment).
    public IReadOnlyList<ReadyAttachment> TakeReady()
    {
        // No owner yet means nothing was ever attached in this view.
        if (ownerId is null)
        {
            return [];
        }

        return registry.TakeReady(ownerId, composerKey, uploadIds);
    }
}
