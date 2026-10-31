using System.Text;

namespace kisatsingen.OfficeConversion;

// The Markdown a conversion writes, refused as soon as it passes the most
// characters the caller can use. Checked on every write rather than at the
// end: a few bytes of XML can ask for millions of table cells, and only the
// output's length bounds the memory a conversion takes.
internal sealed class MarkdownOutput(int maxCharacters)
{
    private readonly StringBuilder _markdown = new();

    public void Append(string text)
    {
        if (text.Length > maxCharacters - _markdown.Length)
        {
            throw new MarkdownTooLongException(maxCharacters);
        }

        _markdown.Append(text);
    }

    public override string ToString() => _markdown.ToString();
}

// Thrown by MarkdownOutput and caught where a conversion starts, which turns
// it into a refusal; nothing in between has to pass it along.
internal sealed class MarkdownTooLongException(int maxCharacters)
    : Exception($"The Markdown would pass the limit of {maxCharacters} characters.");
