using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class TextSegmentView : ComponentBase
{
    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required ILogger<TextSegmentView> Logger { get; set; }

    [Parameter, EditorRequired] public required TextSegment Segment { get; set; }

    // The browser owns the element's content while this is true.
    [Parameter] public bool IsStreaming { get; set; }

    private ElementReference _element;
    private string? _lastRendered;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsStreaming || _lastRendered == Segment.Text)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("chatClient.renderMarkdown", _element, Segment.Text);
            _lastRendered = Segment.Text;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.renderMarkdown");
        }
    }
}
