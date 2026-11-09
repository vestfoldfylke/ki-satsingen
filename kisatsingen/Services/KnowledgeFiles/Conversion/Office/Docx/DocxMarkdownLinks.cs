namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

internal static class DocxMarkdownLinks
{
    // Text is inline Markdown, such as "**Søknad**", already escaped by
    // DocxInlineMarkdown, so a bracket in it can't end it early.
    public static string Link(string text, string url) => $"[{text}]({EscapeUrl(url)})";

    // The image itself isn't in the Markdown, so it has no address; its
    // description is what the reader gets of it.
    public static string Image(string description) =>
        $"![{DocxMarkdownEscape.Inline(description.ReplaceLineEndings(" ").Trim())}]()";

    // A space or a parenthesis would end the address early, and an angle
    // bracket would start one written as <...>.
    private static string EscapeUrl(string url) => url
        .Replace(" ", "%20")
        .Replace("(", "%28")
        .Replace(")", "%29")
        .Replace("<", "%3C")
        .Replace(">", "%3E");
}
