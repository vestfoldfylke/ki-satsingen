using kisatsingen.Data.Entities;

namespace kisatsingen.Services.Chat;

// What processing made of one sent file. The turn records it as a
// TurnAttachment, which keeps only the file's id; the chat lists the whole
// file, so Available carries it.
internal abstract record TurnAttachmentResult
{
    private TurnAttachmentResult() { }

    public sealed record Available(KnowledgeFileMetadata File) : TurnAttachmentResult;

    public sealed record Unavailable(string Reason) : TurnAttachmentResult;
}
