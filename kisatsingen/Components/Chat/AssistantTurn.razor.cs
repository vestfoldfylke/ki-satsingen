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

    // The chosen name leads and the provider's id follows as the precise answer.
    // The key stands in for a name the catalogue no longer has. Blanks count as
    // absent, so nothing renders as " ()".
    private static string? DescribeModel(TurnMetadata meta)
    {
        var name = !string.IsNullOrWhiteSpace(meta.ModelDisplayName)
            ? meta.ModelDisplayName
            : meta.ModelKey?.Value;
        var providerId = !string.IsNullOrWhiteSpace(meta.ModelId) ? meta.ModelId : null;

        return (name, providerId) switch
        {
            (not null, not null) => $"{name} ({providerId})",
            (not null, null) => name,
            (null, not null) => providerId,
            _ => null
        };
    }

    private static string FormatMs(long ms) => ms < 1000
        ? $"{ms} ms"
        : $"{ms / 1000.0:0.##} s";

    protected override void OnParametersSet()
    {
        if (_markdownRefs is null || _markdownRefs.Length != Turn.Parts.Count)
        {
            _markdownRefs = new ElementReference[Turn.Parts.Count];
            Array.Resize(ref _lastRendered, Turn.Parts.Count);
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
