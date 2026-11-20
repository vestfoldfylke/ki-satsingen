using System.ComponentModel;
using kisatsingen.AIFunctions.PresentationTools;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions;

internal static class ChatTools
{
    public const string GetCurrentTimeUtcToolName = "get_current_time_utc";

    public static readonly AIFunction GetCurrentTimeUtcTool = AIFunctionFactory.Create(
        GetCurrentTimeUtc,
        name: GetCurrentTimeUtcToolName,
        description: "Returns the current UTC time as an ISO 8601 string. Use this when the user asks about the current time.");

    // Given to every selectable model: an invariant, not a per-model flag that
    // could be set wrong. Declared after the tools it names, because static
    // initialisers run in textual order. The presentation tool is here rather
    // than gated like the file tools: any message may be text to turn into
    // slides, and a tool list that stays the same keeps the prompt cache valid.
    public static readonly IReadOnlyList<AITool> All = [GetCurrentTimeUtcTool, CreatePresentationTool.Tool, ListPresentationPicturesTool.Tool];

    [Description("Returns the current UTC time as an ISO 8601 string.")]
    private static string GetCurrentTimeUtc()
        => DateTimeOffset.UtcNow.ToString("O");
}
