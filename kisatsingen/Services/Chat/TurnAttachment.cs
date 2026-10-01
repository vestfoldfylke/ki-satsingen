using System.Text.Json.Serialization;

namespace kisatsingen.Services.Chat;

// One file attached to a turn. While it is processed, neither FileId nor
// UnavailableReason is set; once processing ends, exactly one is: the file
// was saved, or it is unavailable to the model and the reason says why.
// FileId is the id of the knowledge file it was saved as, named as the
// tools name it.
public sealed record TurnAttachment(string FileName, Guid? FileId = null, string? UnavailableReason = null)
{
    // Derived, so not stored: AttachmentsJson holds the name and the file id or reason only.
    [JsonIgnore]
    public bool IsProcessing => FileId is null && UnavailableReason is null;

    public static TurnAttachment Processing(string fileName) => new(fileName);

    public TurnAttachment SavedAs(Guid fileId) => this with { FileId = fileId };

    public TurnAttachment Unavailable(string reason) => this with { UnavailableReason = reason };
}
