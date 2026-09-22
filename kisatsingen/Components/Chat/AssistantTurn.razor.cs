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

    // Three names for the same model, any subset of which may be missing:
    //
    //   ModelDisplayName  what the user chose        "Rask"
    //   ModelId           what the provider served   "gpt-4o-mini-2024-07-18"
    //   ModelKey          our own stable identifier  "fast"
    //
    // The friendly name leads when there is one — the provider id is the precise
    // answer, but it is not the one that was chosen — with the id in parentheses
    // behind it. The key stands in for the name on rows written before the
    // catalogue knew the model, or by a model that has since left it. Empty and
    // whitespace count as absent, so a blank never renders as " ()" around an id.
    //
    // Null only when the turn names no model in any form at all, which is what
    // drops the row entirely.
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
