using System.Text.Json.Serialization;

namespace kisatsingen.Services.WordDocuments;

// The kinds of content a Word document is built from, one template style each.
// Which fields a kind takes, and which templates have it, is decided by
// DocumentBlockFields.
[JsonConverter(typeof(JsonStringEnumConverter<DocumentBlockKind>))]
public enum DocumentBlockKind
{
    Kicker,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Lead,
    Paragraph,
    BulletList,
    NumberedList,
    Table,
    Caption,
    FactBox,
    PageBreak
}
