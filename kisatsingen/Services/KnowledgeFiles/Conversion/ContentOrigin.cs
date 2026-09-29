namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Where a file's Markdown comes from: what was uploaded and, when that is not
// enough, how its text was obtained. A plain fact about the file; how far to
// trust it is told to the model as a note derived from this in the tools, so
// the wording can improve without touching stored data.
//
// Recorded rather than derived from the content type: once one type has two
// routes (a PDF's text layer or OCR; a placeholder or a description), files
// converted one way could never be told apart from the others, since
// originals are not kept. A new route adds its value in the same change.
//
// Persisted by name once stored: add freely, never rename.
public enum ContentOrigin
{
    // The upload was a text file (.txt, .md), used as it is: exact.
    TextFile,

    // The upload was an image whose content is not read.
    ImagePlaceholder
}
