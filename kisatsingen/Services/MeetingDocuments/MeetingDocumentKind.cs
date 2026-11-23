using System.Text.Json.Serialization;

namespace kisatsingen.Services.MeetingDocuments;

// Whether the meeting template is filled as a notice or as minutes; it sets
// the document's heading.
[JsonConverter(typeof(JsonStringEnumConverter<MeetingDocumentKind>))]
public enum MeetingDocumentKind
{
    Notice,
    Minutes
}
