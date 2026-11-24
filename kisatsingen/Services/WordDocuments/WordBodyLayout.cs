using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Where a template's built content goes: before InsertBefore, in the section
// whose page size and margins set the width of tables.
internal sealed record WordBodyLayout(OpenXmlElement InsertBefore, SectionProperties ContentSection);
