using System.Text.Json.Serialization;

namespace kisatsingen.Services.Chat;

// One file attached to a turn: processing, then available or unavailable.
// While it is processed, neither FileId nor UnavailableReason is set; after,
// exactly one is. Available means the model can read it as FileId: a file
// saved for this turn, or the one already in the chat with the same content.
// FileId is the knowledge file's id, named as the tools name it.
public sealed record TurnAttachment(string FileName, Guid? FileId = null, string? UnavailableReason = null)
{
    // Derived, so not stored: AttachmentsJson holds the name and the file id or reason only.
    [JsonIgnore]
    public bool IsProcessing => FileId is null && UnavailableReason is null;

    public static TurnAttachment Processing(string fileName) => new(fileName);

    public TurnAttachment Available(Guid fileId) => this with { FileId = fileId };

    public TurnAttachment Unavailable(string reason) => this with { UnavailableReason = reason };
}
