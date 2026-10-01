using System.Text;

namespace kisatsingen.Services.Chat;

// Rendered from the turn's own snapshot, never a live lookup, so a replayed
// turn says exactly what it said when sent, even after a file is deleted.
internal static class AttachmentLine
{
    // Null when there is nothing to say.
    public static string? Render(IReadOnlyList<TurnAttachment> attachments)
    {
        var shown = attachments.Where(attachment => !attachment.IsProcessing).ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        var line = new StringBuilder(AttachmentTexts.Heading);
        foreach (var attachment in shown)
        {
            line.Append('\n').Append(EntryFor(attachment));
        }

        if (shown.Any(attachment => attachment.FileId is not null))
        {
            line.Append('\n').Append(AttachmentTexts.ReadBeforeAnswering);
        }

        return line.ToString();
    }

    private static string EntryFor(TurnAttachment attachment)
    {
        if (attachment.FileId is Guid fileId)
        {
            return AttachmentTexts.AvailableEntry(attachment.FileName, fileId);
        }

        return AttachmentTexts.UnavailableEntry(attachment.FileName, attachment.UnavailableReason!);
    }
}
