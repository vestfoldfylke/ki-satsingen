using Microsoft.AspNetCore.Components;

namespace kisatsingen.Services.Markdown;

public interface IMarkdownRenderer
{
    MarkupString Render(string markdown);
}