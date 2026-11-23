namespace kisatsingen.Services.MeetingDocuments;

// Title becomes the file's document title (an accessibility requirement) and
// its file name, so it is the one text field required. The other fields fill the template's form; a field left out
// stays blank, for the user to fill in Word.
public sealed record MeetingDocumentSpec(
    string Title,
    MeetingDocumentKind Kind,
    string? Meeting,
    IReadOnlyList<AgendaItemSpec> AgendaItems,
    string? Date = null,
    string? Time = null,
    string? Place = null,
    string? Invited = null,
    string? Absent = null,
    string? MinuteTaker = null);
