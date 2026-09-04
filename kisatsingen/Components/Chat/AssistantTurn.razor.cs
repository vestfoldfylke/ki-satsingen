using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class AssistantTurn : ComponentBase
{
    [Inject]
    public required IJSRuntime JS { get; set; }
    [Inject]
    public required ILogger<AssistantTurn> Logger { get; set; }

    [Parameter, EditorRequired] public required AssistantTurnView Turn { get; set; }

    private ElementReference[]? _markdownRefs;
    private string?[] _lastRendered = [];

    private string PopoverId => $"assistant-turn-metadata-{Turn.Id}";

    private bool HasCopyableText => Turn.Parts.Any(p => !string.IsNullOrEmpty(p.Text));

    private static string FormatMs(long ms) => ms < 1000
        ? $"{ms} ms"
        : $"{ms / 1000.0:0.##} s";

    protected override void OnParametersSet()
    {
        if (_markdownRefs is null || _markdownRefs.Length != Turn.Parts.Count)
        {
            _markdownRefs = new ElementReference[Turn.Parts.Count];
            _lastRendered = new string?[Turn.Parts.Count];
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_markdownRefs is null)
        {
            return;
        }

        for (var i = 0; i < Turn.Parts.Count; i++)
        {
            var text = Turn.Parts[i].Text;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            if (_lastRendered[i] == text)
            {
                continue;
            }

            try
            {
                await JS.InvokeVoidAsync("chatClient.renderMarkdown", _markdownRefs[i], text);
                _lastRendered[i] = text;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.renderMarkdown");
            }
        }
    }
}