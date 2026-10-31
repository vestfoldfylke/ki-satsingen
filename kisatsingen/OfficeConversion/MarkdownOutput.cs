using System.Text;

namespace kisatsingen.OfficeConversion;

// The Markdown a conversion writes, refused as soon as it would exceed
// maxCharacters. Checked on every write rather than at the end: a few bytes
// of XML can ask for millions of table cells, and only the output's length
// bounds the memory a conversion takes.
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

// Thrown by MarkdownOutput when a write would exceed its maxCharacters.
internal sealed class MarkdownTooLongException(int maxCharacters)
    : Exception($"The Markdown would exceed the limit of {maxCharacters} characters.");
