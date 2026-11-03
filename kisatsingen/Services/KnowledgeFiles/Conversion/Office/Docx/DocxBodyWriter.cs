using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using static kisatsingen.Services.KnowledgeFiles.Conversion.Office.OpenXmlValues;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes the document body as Markdown: headings, paragraphs, lists and
// tables in document order, then the notes the body referenced.
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

    private const int IndentPerListLevel = 4;
    private const string BulletMarker = "- ";

    // Markdown numbers a list itself, so every item can be "1.".
    private const string NumberMarker = "1. ";

    private const string HardLineBreak = "  \n";
    private const string QuoteMarker = "> ";

    private readonly DocxContent _content;
    private readonly DocxBlockOutput _blocks;
    private readonly DocxParagraphReader _reader;
    private readonly DocxBlockText _cellText;
    private readonly CancellationToken _ct;
    private int _previousListLevel;

    private DocxBodyWriter(DocxContent content, DocxBlockOutput blocks, DocxParagraphReader reader, CancellationToken ct)
    {
        _content = content;
        _blocks = blocks;
        _reader = reader;
        _cellText = new DocxBlockText(reader, ct);
        _ct = ct;
    }

    // Returns how many characters of the document's text it wrote, to compare with DocxTextCount.
    public static int Write(DocxContent content, MarkdownOutput output, CancellationToken ct)
    {
        var blocks = new DocxBlockOutput(output);
        var tally = new DocxTextTally();
        var noteReferences = new DocxNoteReferences(content.Footnotes, content.Endnotes);

        var bodyReader = new DocxParagraphReader(content.Styles, content.Hyperlinks, noteReferences, tally, ct);
        new DocxBodyWriter(content, blocks, bodyReader, ct).WriteChildren(content.Body);

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
                _blocks.Table(DocxTables.RowsOf(table, _cellText.CellTextOf, _ct));
                break;
            default:
                WriteChildren(element);
                break;
        }
    }

    // A page break inside the paragraph starts a new paragraph: plain after a
    // heading or a list item, still quoted in a quote.
    private void WriteParagraph(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var headingLevel = HeadingLevelOf(properties, styleId);
        var isQuote = _content.Styles.IsQuote(styleId);

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
                case true when ListItemOf(properties, styleId) is { } listItem:
                    WriteListItem(listItem, lines);
                    break;
                case var _ when isQuote:
                    WriteQuote(lines);
                    break;
                default:
                    _blocks.Paragraph(string.Join(HardLineBreak, lines.Select(DocxInlineMarkdown.EscapeBlockStart)));
                    break;
            }
        }

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
        _blocks.Paragraph(QuoteMarker + string.Join(HardLineBreak + QuoteMarker, lines.Select(DocxInlineMarkdown.EscapeBlockStart)));
    }

    // A list may skip levels in Word, but four spaces too many make a code
    // block in Markdown, so an item is at most one level below the one before.
    private void WriteListItem((DocxListKind Kind, int Level) listItem, List<string> lines)
    {
        var maxLevel = _blocks.IsAfterListItem
            ? _previousListLevel + 1
            : 0;
        var level = Math.Clamp(listItem.Level, 0, maxLevel);
        _previousListLevel = level;

        var indent = new string(' ', level * IndentPerListLevel);
        var marker = listItem.Kind == DocxListKind.Bulleted
            ? BulletMarker
            : NumberMarker;
        var continuationIndent = new string(' ', indent.Length + marker.Length);

        var text = string.Join(HardLineBreak + continuationIndent, lines.Select(DocxInlineMarkdown.EscapeBlockStart));
        _blocks.ListItem($"{indent}{marker}{text}");
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

    // The paragraph's own list wins over its style's. A paragraph that sets
    // only its level is at that level of its style's list.
    private (DocxListKind Kind, int Level)? ListItemOf(ParagraphProperties? properties, string? styleId)
    {
        var ownList = properties?.NumberingProperties;
        var ownListId = ValueOf(ownList?.NumberingId?.Val);
        var ownLevel = ValueOf(ownList?.NumberingLevelReference?.Val);
        var styleList = _content.Styles.ListOf(styleId);

        var listId = ownListId ?? styleList?.ListId;
        if (listId is null)
        {
            return null;
        }

        var level = ownLevel ?? (ownListId is null ? styleList?.Level : null) ?? 0;
        return _content.Lists.KindOf(new DocxListReference(listId.Value, level)) is { } kind
            ? (kind, level)
            : null;
    }
}
