using Microsoft.AspNetCore.Components;

public interface IMarkdownRenderer
{
    MarkupString Render(string markdown);
}