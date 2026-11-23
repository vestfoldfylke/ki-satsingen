using kisatsingen.Services.MeetingDocuments;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.WordDocuments;

namespace kisatsingen.Services.GeneratedFiles;

// A file a tool call generated, as the download button offers it: the spec it
// is built from on each click, and the name it is saved under.
public abstract record GeneratedFile(string FileName)
{
    public sealed record Presentation(string FileName, PresentationSpec Spec) : GeneratedFile(FileName);

    public sealed record WordDocument(string FileName, WordDocumentSpec Spec) : GeneratedFile(FileName);

    public sealed record MeetingDocument(string FileName, MeetingDocumentSpec Spec) : GeneratedFile(FileName);
}
