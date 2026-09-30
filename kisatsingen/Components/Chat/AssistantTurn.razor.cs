using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class AssistantTurn : ComponentBase
{
    [Parameter, EditorRequired] public required Turn Turn { get; set; }

    [Parameter] public bool IsLive { get; set; }

    private string PopoverId => $"assistant-turn-metadata-{Turn.Id}";

    private bool HasCopyableText => Turn.Answer.OfType<TextSegment>().Any(text => !string.IsNullOrEmpty(text.Text));

    private Guid? StreamingSegmentId => IsLive && Turn.Answer is [.., TextSegment last] ? last.Id : null;

    // Blanks count as absent, so nothing renders as " ()".
    private static string? DescribeModel(Turn turn)
    {
        var name = !string.IsNullOrWhiteSpace(turn.ModelDisplayName)
            ? turn.ModelDisplayName
            : turn.ModelKey?.Value;
        var providerId = !string.IsNullOrWhiteSpace(turn.Metadata?.ServedModelId) ? turn.Metadata.ServedModelId : null;

        return (name, providerId) switch
        {
            (not null, not null) => $"{name} ({providerId})",
            (not null, null) => name,
            (null, not null) => providerId,
            _ => null
        };
    }

    private static string FormatTokens(long tokens, bool isEstimated) =>
        isEstimated ? $"~{tokens:N0}" : tokens.ToString("N0");

    private static string FormatMs(long ms) => ms < 1000
        ? $"{ms} ms"
        : $"{ms / 1000.0:0.##} s";
}
