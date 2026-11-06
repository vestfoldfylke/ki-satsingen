using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// A paragraph's own text as pieces, and the blocks nested in it, such as a
// text box's paragraphs, which are written after it rather than inside it.
internal sealed record DocxParagraphContent(IReadOnlyList<DocxPiece> Pieces, IReadOnlyList<OpenXmlElement> NestedBlocks);

// Reads a paragraph's runs into pieces. One reader per part, since each part
// has its own hyperlinks.
//
// Recursive, which is safe: CheckedParts refuses a part nested more than 256 deep.
// ct is checked per element, since one paragraph can hold most of a part.
internal sealed class DocxParagraphReader(DocxStyles styles, DocxHyperlinks hyperlinks, DocxNoteReferences noteReferences, DocxTextTally tally, CancellationToken ct)
{
    private const string LineBreak = "\n";
    private const string PageBreakType = "page";

    // Within a paragraph, a tab is spacing; at a line's start it would make a code block.
    private const string TabReplacement = " ";
    private const string NonBreakingHyphen = "-";

    // A heading's text has no bold or italic: the # already says it is
    // emphasized. Strikethrough is kept, since struck text is no longer valid.
    public DocxParagraphContent Read(Paragraph paragraph, bool allowsEmphasis)
    {
        var scope = new Scope(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value, allowsEmphasis, [], []);
        Collect(paragraph, default, scope);
        return new DocxParagraphContent(scope.Pieces, scope.NestedBlocks);
    }

    private void Collect(OpenXmlElement container, RunFormatting formatting, Scope scope)
    {
        foreach (var element in container.ChildElements)
        {
            ct.ThrowIfCancellationRequested();

            if (DocxSkippedText.IsSkipped(element))
            {
                continue;
            }

            switch (element)
            {
                case ParagraphProperties or RunProperties or SdtProperties:
                    break;
                // A text box's paragraphs and tables, nested in a run.
                case Paragraph or Table:
                    scope.NestedBlocks.Add(element);
                    break;
                case Run run:
                    Collect(run, FormattingOf(run, scope), scope);
                    break;
                case Text text:
                    tally.Add(text.Text);
                    scope.Pieces.Add(new DocxPiece(text.Text, formatting));
                    break;
                case TabChar or PositionalTab:
                    scope.Pieces.Add(new DocxPiece(TabReplacement, formatting));
                    break;
                case Break lineBreak when lineBreak.Type?.InnerText == PageBreakType:
                    scope.Pieces.Add(DocxPiece.ParagraphBreak);
                    break;
                case Break or CarriageReturn:
                    scope.Pieces.Add(new DocxPiece(LineBreak, formatting));
                    break;
                case NoBreakHyphen:
                    scope.Pieces.Add(new DocxPiece(NonBreakingHyphen, formatting));
                    break;
                case FootnoteReference reference:
                    AddNoteReference(DocxNoteKind.Footnote, reference.Id, formatting, scope);
                    break;
                case EndnoteReference reference:
                    AddNoteReference(DocxNoteKind.Endnote, reference.Id, formatting, scope);
                    break;
                case Hyperlink hyperlink:
                    AddHyperlink(hyperlink, formatting, scope);
                    break;
                default:
                    Collect(element, formatting, scope);
                    break;
            }
        }
    }

    // The run's own settings win over its character style, which wins over the
    // paragraph style. The paragraph mark's settings (pPr/rPr) are left out:
    // they format the mark, not the text.
    private RunFormatting FormattingOf(Run run, Scope scope)
    {
        var runProperties = run.RunProperties;
        var styleFormatting = styles.FormattingOf(scope.ParagraphStyleId, runProperties?.RunStyle?.Val?.Value);

        // A run setting either strikethrough decides for itself. Switching off
        // one kind while the style sets the other is rare enough not to model.
        var ownStrike = DocxToggle.Read(runProperties?.Strike);
        var ownDoubleStrike = DocxToggle.Read(runProperties?.DoubleStrike);
        var isStruck = ownStrike is null && ownDoubleStrike is null
            ? styleFormatting.IsStruck
            : ownStrike == true || ownDoubleStrike == true;

        var formatting = new RunFormatting(
            IsBold: DocxToggle.Read(runProperties?.Bold) ?? styleFormatting.IsBold,
            IsItalic: DocxToggle.Read(runProperties?.Italic) ?? styleFormatting.IsItalic,
            IsStruck: isStruck);

        return scope.AllowsEmphasis
            ? formatting
            : formatting with { IsBold = false, IsItalic = false };
    }

    private void AddNoteReference(DocxNoteKind kind, IntegerValue? id, RunFormatting formatting, Scope scope)
    {
        if (OpenXmlValues.ValueOf(id) is { } noteId && noteReferences.Reference(kind, noteId) is { } label)
        {
            scope.Pieces.Add(new DocxPiece(label, formatting));
        }
    }

    // A link to a bookmark (w:anchor) or to an address the part doesn't have
    // stays plain text.
    private void AddHyperlink(Hyperlink hyperlink, RunFormatting formatting, Scope scope)
    {
        var url = hyperlink.Id?.Value is { } relationshipId
            ? hyperlinks.UrlsById.GetValueOrDefault(relationshipId)
            : null;
        if (url is null)
        {
            Collect(hyperlink, formatting, scope);
            return;
        }

        var linkScope = scope with { Pieces = [] };
        Collect(hyperlink, formatting, linkScope);

        var text = DocxInlineMarkdown.Write(linkScope.Pieces).ReplaceLineEndings(" ").Trim();
        if (text.Length > 0)
        {
            scope.Pieces.Add(new DocxPiece(DocxMarkdownLinks.Link(text, url), default));
        }
    }

    private sealed record Scope(string? ParagraphStyleId, bool AllowsEmphasis, List<DocxPiece> Pieces, List<OpenXmlElement> NestedBlocks);
}
