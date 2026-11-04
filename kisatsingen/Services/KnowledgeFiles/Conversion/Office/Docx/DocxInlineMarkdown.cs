using System.Text;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Text from a paragraph's runs, with the formatting it has. Text may hold
// "\n" for a line break; IsParagraphBreak is a page break, which ends the
// paragraph and starts a new one.
internal readonly record struct DocxPiece(string Text, RunFormatting Formatting, bool IsParagraphBreak = false)
{
    public static readonly DocxPiece ParagraphBreak = new("", default, IsParagraphBreak: true);
}

// Writes pieces as inline Markdown: their text with **, * and ~~ around the
// formatted parts. Lines are separated by "\n" and paragraphs by "\n\n"; the
// caller decides how a block writes them.
//
// A marker opens when the formatting starts and closes when it ends, not per
// piece: Word splits text into runs at every edit, and markers per run give
// "**H****envisning**". Text is not escaped otherwise: a stray * in the text
// shows as a literal * at worst, which a reader reads past.
internal static class DocxInlineMarkdown
{
    private const string BoldMarker = "**";
    private const string ItalicMarker = "*";
    private const string StrikeMarker = "~~";
    private const string ParagraphSeparator = "\n\n";
    private const char LineBreak = '\n';
    private const char Space = ' ';

    public static string Write(IReadOnlyList<DocxPiece> pieces)
    {
        var markdown = new StringBuilder();
        var openMarkers = new List<string>();

        // Whitespace is written only once the next text shows whether a
        // marker closes before it, so markers sit outside it: "**Viktig** tekst".
        var pendingWhitespace = new StringBuilder();

        foreach (var piece in pieces)
        {
            if (piece.IsParagraphBreak)
            {
                Close(markdown, openMarkers, keptCount: 0);
                markdown.Append(ParagraphSeparator);
                pendingWhitespace.Clear();
                continue;
            }

            var leadingLength = piece.Text.Length - piece.Text.TrimStart().Length;
            if (leadingLength == piece.Text.Length)
            {
                pendingWhitespace.Append(piece.Text);
                continue;
            }

            var trailingLength = piece.Text.Length - piece.Text.TrimEnd().Length;
            var text = piece.Text[leadingLength..^trailingLength];
            pendingWhitespace.Append(piece.Text[..leadingLength]);

            var markers = MarkersOf(piece.Formatting);
            var keptCount = openMarkers.TakeWhile(markers.Contains).Count();
            var markersToOpen = markers.Except(openMarkers.Take(keptCount)).ToList();

            if (keptCount < openMarkers.Count)
            {
                var isNextToText = pendingWhitespace.Length == 0 && markersToOpen.Count == 0;
                var needsSpace = isNextToText && IsPunctuation(markdown[^1]) && IsWordCharacter(text[0]);
                Close(markdown, openMarkers, keptCount);
                if (needsSpace)
                {
                    pendingWhitespace.Append(Space);
                }
            }

            // Two line breaks in a row would read as a paragraph break, which
            // would split an open marker from its closing one.
            var whitespace = pendingWhitespace.ToString();
            markdown.Append(whitespace.Contains(LineBreak)
                ? LineBreak
                : whitespace);

            pendingWhitespace.Clear();

            if (markersToOpen.Count > 0)
            {
                if (IsPunctuation(text[0]) && markdown.Length > 0 && IsWordCharacter(markdown[^1]))
                {
                    markdown.Append(Space);
                }

                markdown.AppendJoin("", markersToOpen);
                openMarkers.AddRange(markersToOpen);
            }

            markdown.Append(text);
            pendingWhitespace.Append(piece.Text[^trailingLength..]);
        }

        Close(markdown, openMarkers, keptCount: 0);
        return markdown.ToString();
    }

    // Splits what Write wrote into its paragraphs, each its trimmed lines.
    // Empty lines and paragraphs are dropped.
    public static List<List<string>> ParagraphsOf(string markdown) => markdown
        .Split(ParagraphSeparator)
        .Select(paragraph => paragraph
            .Split(LineBreak)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList())
        .Where(lines => lines.Count > 0)
        .ToList();

    // A line starting with # would become a heading, and DocumentOutline makes
    // sections from headings; one starting with > would become a quote.
    public static string EscapeBlockStart(string line) =>
        line.StartsWith('#') || line.StartsWith('>') || line.All(character => character == '=') || line.All(character => character == '-')
            ? $"\\{line}"
            : line;

    // In opening order, so bold and italic together give "***text***".
    private static List<string> MarkersOf(RunFormatting formatting)
    {
        var markers = new List<string>();
        if (formatting.IsBold)
        {
            markers.Add(BoldMarker);
        }

        if (formatting.IsItalic)
        {
            markers.Add(ItalicMarker);
        }

        if (formatting.IsStruck)
        {
            markers.Add(StrikeMarker);
        }

        return markers;
    }

    // Closes the markers opened after the first keptCount, innermost first.
    private static void Close(StringBuilder markdown, List<string> openMarkers, int keptCount)
    {
        for (var index = openMarkers.Count - 1; index >= keptCount; index--)
        {
            markdown.Append(openMarkers[index]);
        }

        openMarkers.RemoveRange(keptCount, openMarkers.Count - keptCount);
    }

    // CommonMark doesn't close a marker that follows punctuation when a letter
    // comes right after it ("**org.nr.**929" stays literal), nor open one that
    // precedes punctuation right after a letter. A space between them lets it.
    private static bool IsPunctuation(char character) => char.IsPunctuation(character) || char.IsSymbol(character);

    private static bool IsWordCharacter(char character) => !char.IsWhiteSpace(character) && !IsPunctuation(character);
}
