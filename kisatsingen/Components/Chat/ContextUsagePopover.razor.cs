using System.Globalization;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class ContextUsagePopover : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-context-usage";
    private const string MeterId = "composer-context-usage-meter";

    private const double FillingThresholdRatio = 0.8;

    // No request culture is configured, so N0 would follow the server's locale.
    private static readonly CultureInfo Norwegian = CultureInfo.GetCultureInfo("nb-NO");

    [Parameter, EditorRequired]
    public required ChatModel Model { get; set; }

    [Parameter, EditorRequired]
    public required long EstimatedContextTokens { get; set; }

    [Parameter]
    public MessageUsage? Usage { get; set; }

    private long FillingThresholdTokens => (long)(Model.ContextWindowTokens * FillingThresholdRatio);

    private string StatusText =>
        Model.WouldOverflow(EstimatedContextTokens) ? "Samtalen er for lang for modellen"
        : EstimatedContextTokens >= FillingThresholdTokens ? "Samtalen nærmer seg grensen"
        : "Samtalen har god plass";

    private static string Format(long tokens) => tokens.ToString("N0", Norwegian);
}
