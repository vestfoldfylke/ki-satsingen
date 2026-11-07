using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using static kisatsingen.Services.KnowledgeFiles.Conversion.Office.OpenXmlValues;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes the document body as Markdown: headings, paragraphs, lists, quotes,
// code and tables in document order, then the notes the body referenced.
//
// Walks into every element it doesn't know, so wrappers such as content
// controls keep their text. Recursive, which is safe: CheckedParts refuses a
// part nested more than 256 deep.
internal sealed class DocxBodyWriter
{
    // Outline levels 0-8 are heading levels 1-9; 9 marks a paragraph as body text.
    private const int DeepestOutlineLevel = 8;
    private const int BodyTextOutlineLevel = 9;
    private const int DeepestMarkdownHeading = 6;

    private const string HardLineBreak = "  \n";
    private const string QuoteMarker = "> ";

    private readonly DocxContent _content;
    private readonly DocxBlockOutput _blocks;
    private readonly DocxParagraphReader _reader;
    private readonly DocxBlockText _cellText;
    private readonly DocxListWriter _lists;
    private readonly CancellationToken _ct;

    // Code paragraphs in a row, written as one code block when something else comes.
    private readonly List<string> _codeLines = [];

    private DocxBodyWriter(DocxContent content, DocxBlockOutput blocks, DocxParagraphReader reader, CancellationToken ct)
    {
        _content = content;
        _blocks = blocks;
        _reader = reader;
        _cellText = new DocxBlockText(reader, ct);
        _lists = new DocxListWriter(content, blocks);
        _ct = ct;
    }

    // Returns how many characters of the document's text it wrote, to compare with DocxTextCount.
    public static int Write(DocxContent content, MarkdownOutput output, CancellationToken ct)
    {
        var blocks = new DocxBlockOutput(output);
        var tally = new DocxTextTally();
        var noteReferences = new DocxNoteReferences(content.Footnotes, content.Endnotes);

        var bodyReader = new DocxParagraphReader(content.Styles, content.Hyperlinks, noteReferences, tally, ct);
        var bodyWriter = new DocxBodyWriter(content, blocks, bodyReader, ct);
        bodyWriter.WriteChildren(content.Body);
        bodyWriter.WriteCodeBlock();

        var footnoteText = new DocxBlockText(new DocxParagraphReader(content.Styles, content.Footnotes.Hyperlinks, noteReferences, tally, ct), ct);
        var endnoteText = new DocxBlockText(new DocxParagraphReader(content.Styles, content.Endnotes.Hyperlinks, noteReferences, tally, ct), ct);
        DocxNotesWriter.Write(
            noteReferences,
            kind => kind == DocxNoteKind.Footnote ? footnoteText : endnoteText,
            blocks);

        return tally.Characters;
    }

    private void WriteChildren(OpenXmlElement container)
    {
        foreach (var element in container.ChildElements)
        {
            WriteBlock(element);
        }
    }

    // ct is checked per element, not per paragraph: a body can be all
    // wrappers, such as content controls, with no paragraph to check at.
    private void WriteBlock(OpenXmlElement element)
    {
        _ct.ThrowIfCancellationRequested();

        if (DocxSkippedText.IsSkipped(element))
        {
            return;
        }

        switch (element)
        {
            case Paragraph paragraph:
                WriteParagraph(paragraph);
                break;
            case Table table:
                WriteCodeBlock();
                _blocks.Table(DocxTables.RowsOf(table, _cellText.CellTextOf, _ct));
                break;
            default:
                WriteChildren(element);
                break;
        }
    }

    // A page break inside the paragraph starts a new paragraph: plain after a
    // heading, still quoted in a quote, and part of the item in a list.
    private void WriteParagraph(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        if (_content.Styles.IsCode(styleId))
        {
            AddCodeLines(paragraph);
            return;
        }

        WriteCodeBlock();

        var headingLevel = HeadingLevelOf(properties, styleId);
        var isQuote = _content.Styles.IsQuote(styleId);
        var listItem = _lists.ItemOf(properties, styleId);
        var continuesItem = headingLevel is null && (listItem is not null || _lists.ContinuesItem(properties, styleId));

        var content = _reader.Read(paragraph, allowsEmphasis: headingLevel is null);
        var paragraphs = DocxInlineMarkdown.ParagraphsOf(DocxInlineMarkdown.Write(content.Pieces));

        for (var index = 0; index < paragraphs.Count; index++)
        {
            var lines = paragraphs[index];
            var isFirst = index == 0;

            switch (isFirst)
            {
                case true when headingLevel is { } level:
                    WriteHeading(level, lines);
                    break;
                case true when listItem is { } item:
                    _lists.WriteItem(item, lines);
                    break;
                case var _ when isQuote:
                    WriteQuote(lines);
                    break;
                case var _ when continuesItem:
                    _lists.WriteContinuation(lines);
                    break;
                default:
                    _blocks.Paragraph(string.Join(HardLineBreak, lines.Select(DocxMarkdownEscape.BlockStart)));
                    break;
            }
        }

        WriteNestedBlocks(content);
    }

    // Code is kept as it is: its spaces, tabs and empty lines, and no escapes.
    private void AddCodeLines(Paragraph paragraph)
    {
        var content = _reader.Read(paragraph, allowsEmphasis: false);
        var code = string.Concat(content.Pieces.Select(piece => piece.Kind == DocxPieceKind.ParagraphBreak ? "\n" : piece.Text));
        _codeLines.AddRange(code.Split('\n').Select(line => line.TrimEnd()));

        WriteNestedBlocks(content);
    }

    private void WriteCodeBlock()
    {
        if (_codeLines.Any(line => line.Length > 0))
        {
            _blocks.Paragraph(DocxMarkdownCode.Block(_codeLines));
        }

        _codeLines.Clear();
    }

    private void WriteNestedBlocks(DocxParagraphContent content)
    {
        foreach (var nested in content.NestedBlocks)
        {
            WriteBlock(nested);
        }
    }

    // A Markdown heading is one line, so its line breaks become spaces.
    // Word's levels 7-9 are still sections, so they share Markdown's deepest.
    private void WriteHeading(int outlineLevel, List<string> lines)
    {
        var hashes = new string('#', Math.Min(outlineLevel + 1, DeepestMarkdownHeading));
        _blocks.Paragraph($"{hashes} {string.Join(" ", lines)}");
    }

    // Every line gets the marker, so a line break doesn't end the quote.
    private void WriteQuote(List<string> lines)
    {
        _blocks.Paragraph(QuoteMarker + string.Join(HardLineBreak + QuoteMarker, lines.Select(DocxMarkdownEscape.BlockStart)));
    }

    // The paragraph's own outline level wins over its style's, also when it
    // is 9, which makes a paragraph in a heading style body text.
    private int? HeadingLevelOf(ParagraphProperties? properties, string? styleId)
    {
        var outlineLevel = ValueOf(properties?.OutlineLevel?.Val) is >= 0 and <= BodyTextOutlineLevel and var ownLevel
            ? ownLevel
            : _content.Styles.OutlineLevelOf(styleId);

        return outlineLevel is >= 0 and <= DeepestOutlineLevel
            ? outlineLevel
            : null;
    }
}
