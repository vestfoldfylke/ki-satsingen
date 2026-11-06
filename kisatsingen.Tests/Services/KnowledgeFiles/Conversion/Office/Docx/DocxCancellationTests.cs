using DocumentFormat.OpenXml.Wordprocessing;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// The pipeline's timeout works through ct, so every phase that walks a whole
// part checks it. Each test cancels before a phase that once had no check.
public sealed class DocxCancellationTests
{
    private static readonly CancellationToken Cancelled = new(canceled: true);

    // A body of wrappers only, with no paragraph where a check per paragraph could happen.
    private const string WrappersOnly = "<w:sdt><w:sdtContent><w:bookmarkStart w:id=\"0\" w:name=\"merke\"/></w:sdtContent></w:sdt>";

    private static DocxContent ContentWithBody(string bodyXml) => new(
        new Body($"<w:body xmlns:w=\"{WordNamespace}\">{bodyXml}</w:body>"),
        DocxStyles.FromXml(null),
        DocxLists.FromXml(null),
        DocxHyperlinks.None,
        DocxNotes.None,
        DocxNotes.None);

    private static DocxParagraphReader Reader(CancellationToken ct) =>
        new(DocxStyles.FromXml(null), DocxHyperlinks.None, new DocxNoteReferences(DocxNotes.None, DocxNotes.None), new DocxTextTally(), ct);

    // Before the file is even opened, so a broken file isn't reported as unreadable.
    [Fact]
    public void A_cancelled_conversion_stops_before_reading_the_file()
    {
        var notADocx = "Ikke et Word-dokument"u8.ToArray();

        Assert.Throws<OperationCanceledException>(() =>
            DocxConverter.Convert(new MemoryStream(notADocx), new OfficeLimits(long.MaxValue, int.MaxValue, 1), Cancelled));
    }

    [Fact]
    public void Writing_the_body_stops_when_it_is_cancelled_even_without_paragraphs()
    {
        var content = ContentWithBody(WrappersOnly);

        Assert.Throws<OperationCanceledException>(() => DocxBodyWriter.Write(content, new MarkdownOutput(int.MaxValue), Cancelled));
    }

    // One paragraph can hold most of a part.
    [Fact]
    public void Reading_a_paragraph_stops_when_it_is_cancelled()
    {
        var paragraph = new Paragraph($"<w:p xmlns:w=\"{WordNamespace}\"><w:r><w:t>Tekst</w:t></w:r></w:p>");

        Assert.Throws<OperationCanceledException>(() => Reader(Cancelled).Read(paragraph, allowsEmphasis: true));
    }

    [Fact]
    public void Reading_a_cell_or_note_stops_when_it_is_cancelled_even_without_paragraphs()
    {
        var content = ContentWithBody(WrappersOnly);

        Assert.Throws<OperationCanceledException>(() => new DocxBlockText(Reader(CancellationToken.None), Cancelled).ParagraphsOf(content.Body));
    }

    [Fact]
    public void Counting_the_document_s_text_stops_when_it_is_cancelled()
    {
        var content = ContentWithBody(Paragraph("Tekst"));

        Assert.Throws<OperationCanceledException>(() => DocxTextCount.Of(content, Cancelled));
    }
}
