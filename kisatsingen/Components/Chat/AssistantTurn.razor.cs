using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public partial class AssistantTurn : ComponentBase
{
    private static readonly string[] PendingPhrases =
    [
        "Klør seg i hodet",
        "Spisser blyanten",
        "Blar i permene",
        "Tygger på det",
        "Snur alle steiner",
        "Bretter opp ermene",
        "Teller på fingrene",
        "Rører i gryta",
        "Myser på problemet",
        "Legger hodet i bløt",
        "Tenker så det knaker",
        "Prøver å knekke nøtta",
        "Pusser brillene",
        "Sjekker under sofaputene",
        "Rådfører seg med katten",
        "Tar seg en kaffesup",
    ];

    private readonly string _pendingPhrase = PendingPhrases[Random.Shared.Next(PendingPhrases.Length)];

    [Parameter, EditorRequired] public required Turn Turn { get; set; }

    [Parameter] public bool IsLive { get; set; }

    private string PopoverId => $"assistant-turn-metadata-{Turn.Id}";
    private string InstructionsDialogId => $"assistant-turn-instructions-{Turn.Id}";

    private bool IsProcessingAttachments => Turn.Attachments.Any(attachment => attachment.IsProcessing);

    private string ProcessingAttachmentsText
    {
        get
        {
            if (Turn.Attachments.Count == 1)
            {
                return "Behandler vedlegg";
            }

            var current = Turn.Attachments.Count(attachment => !attachment.IsProcessing) + 1;
            return $"Behandler vedlegg {current} av {Turn.Attachments.Count}";
        }
    }

    // A running tool, reasoning or streaming text shows its own progress.
    private bool IsAwaitingModel =>
        IsLive && !IsProcessingAttachments && Turn.Answer is [] or [.., ToolCall { Status: not ToolStatus.Running }];

    private bool HasCopyableText => Turn.Answer.OfType<TextSegment>().Any(text => !string.IsNullOrEmpty(text.Text));

    private Guid? StreamingSegmentId => IsLive && Turn.Answer is [.., TextSegment last] ? last.Id : null;

    // The last reasoning segment counts as running, since text or a tool call ends the
    // reasoning by adding the next segment. Reasoning that ends between a call and the
    // call's result stays last until the next round writes something.
    private Guid? RunningReasoningSegmentId => IsLive && Turn.Answer is [.., ReasoningSegment last] ? last.Id : null;

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
