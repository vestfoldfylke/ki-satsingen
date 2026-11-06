using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class AssistantTurn : ComponentBase
{
    private static readonly string[] PendingPhrases =
    [
        "Våkner fra dvale",
        "Henter en kaffe først",
        "Klør seg i hodet",
        "Spisser blyanten",
        "Blar i permene",
        "Tygger på det",
        "Snur alle steiner",
        "Bretter opp ermene",
        "Teller på fingrene",
        "Rører i gryta",
        "Myser på problemet",
    ];

    private readonly string _pendingPhrase = PendingPhrases[Random.Shared.Next(PendingPhrases.Length)];

    [Parameter, EditorRequired] public required Turn Turn { get; set; }

    [Parameter] public bool IsLive { get; set; }

    private string PopoverId => $"assistant-turn-metadata-{Turn.Id}";
    private string InstructionsDialogId => $"assistant-turn-instructions-{Turn.Id}";

    private bool HasCopyableText => Turn.Answer.OfType<TextSegment>().Any(text => !string.IsNullOrEmpty(text.Text));

    private Guid? StreamingSegmentId => IsLive && Turn.Answer is [.., TextSegment last] ? last.Id : null;

    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

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

    private static string FormatMs(long ms) => ms < 1000
        ? $"{ms} ms"
        : $"{ms / 1000.0:0.##} s";
}
