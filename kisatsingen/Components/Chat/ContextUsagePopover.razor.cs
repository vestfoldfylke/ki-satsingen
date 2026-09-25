using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class ContextUsagePopover : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-context-usage";
    private const string MeterId = "composer-context-usage-meter";

    private const double FillingThresholdRatio = 0.8;
    private const double NearlyFullThresholdRatio = 0.9;

    [Parameter, EditorRequired]
    public required ChatModel Model { get; set; }

    [Parameter, EditorRequired]
    public required long EstimatedContextTokens { get; set; }

    [Parameter]
    public MessageUsage? Usage { get; set; }

    private enum FillLevel
    {
        Roomy,
        Filling,
        NearlyFull,
        Overflowing,
    }

    private long FillingThresholdTokens => (long)(Model.ContextWindowTokens * FillingThresholdRatio);

    private long NearlyFullThresholdTokens => (long)(Model.ContextWindowTokens * NearlyFullThresholdRatio);

    private FillLevel Level =>
        Model.WouldOverflow(EstimatedContextTokens) ? FillLevel.Overflowing
        : EstimatedContextTokens >= NearlyFullThresholdTokens ? FillLevel.NearlyFull
        : EstimatedContextTokens >= FillingThresholdTokens ? FillLevel.Filling
        : FillLevel.Roomy;

    private string StatusText => Level switch
    {
        FillLevel.Overflowing => "Samtalen er for lang for modellen",
        FillLevel.NearlyFull => "Samtalen er nesten full",
        FillLevel.Filling => "Samtalen nærmer seg grensen",
        _ => "Samtalen har god plass",
    };

    private string TriggerColor => Level switch
    {
        FillLevel.Overflowing or FillLevel.NearlyFull => "danger",
        FillLevel.Filling => "warning",
        _ => "primary",
    };

    // The colour alone says nothing to a screen reader.
    private string TriggerLabel => $"Kontekstvindu: {StatusText}. Vis detaljer.";

    private static string Format(long tokens) => tokens.ToString("N0");
}
