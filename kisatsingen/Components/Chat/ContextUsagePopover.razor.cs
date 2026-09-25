using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class ContextUsagePopover : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-context-usage";
    private const string MeterId = "composer-context-usage-meter";

    [Parameter, EditorRequired]
    public required ChatModel Model { get; set; }

    [Parameter, EditorRequired]
    public required long EstimatedContextTokens { get; set; }

    [Parameter]
    public MessageUsage? Usage { get; set; }

    private ContextFillLevel Level => Model.ClassifyContext(EstimatedContextTokens);

    private string StatusText => Level switch
    {
        ContextFillLevel.Overflowing => "Samtalen er for lang for modellen",
        ContextFillLevel.NearlyFull => "Samtalen er nesten full",
        ContextFillLevel.Filling => "Samtalen nærmer seg grensen",
        _ => "Samtalen har god plass",
    };

    private string TriggerColor => Level switch
    {
        ContextFillLevel.Overflowing or ContextFillLevel.NearlyFull => "danger",
        ContextFillLevel.Filling => "warning",
        _ => "primary",
    };

    // The colour alone says nothing to a screen reader.
    private string TriggerLabel => $"Kontekstvindu: {StatusText}. Vis detaljer.";

    private static string Format(long tokens) => tokens.ToString("N0");
}
