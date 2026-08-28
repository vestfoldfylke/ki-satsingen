using Microsoft.AspNetCore.Components;

namespace kisatsingen.Services;

public interface IMarkdownRenderer
{
    MarkupString Render(string markdown);
}