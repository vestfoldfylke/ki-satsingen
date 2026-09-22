using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace kisatsingen.AIFunctions;

internal static class ChatTools
{
    public static readonly AIFunction GetCurrentTimeUtcTool = AIFunctionFactory.Create(
        GetCurrentTimeUtc,
        name: "get_current_time_utc",
        description: "Returns the current UTC time as an ISO 8601 string. Use this when the user asks about the current time.");

    // The tools every user-selectable model is registered with. Tool support is an
    // invariant of the model catalogue rather than a per-model capability —
    // anything a user can pick can call these — so the list is expressed once here
    // and stamped onto every registration centrally. A per-model flag would be a
    // field that is always true, which is an invariant you can violate by accident.
    //
    // Declared after the tools it names: static field initialisers run in textual
    // order, so listing it first would build it out of nulls.
    public static readonly IReadOnlyList<AITool> All = [GetCurrentTimeUtcTool];

    [Description("Returns the current UTC time as an ISO 8601 string.")]
    private static string GetCurrentTimeUtc()
        => DateTimeOffset.UtcNow.ToString("O");
}
