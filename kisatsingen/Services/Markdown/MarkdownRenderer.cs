using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Services.Markdown;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;
    private readonly HtmlSanitizer _sanitizer;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseAutoLinks()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseSoftlineBreakAsHardlineBreak()
            .DisableHtml() // no raw <script>, no raw HTML at all
            .Build();

        _sanitizer = new HtmlSanitizer();
    }

    public MarkupString Render(string markdown)
    {
        var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
        return new MarkupString(_sanitizer.Sanitize(html));
    }
}