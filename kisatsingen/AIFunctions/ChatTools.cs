using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace kisatsingen.Components.Pages;

internal static class ChatTools
{
    public static readonly AIFunction GetCurrentTimeUtcTool = AIFunctionFactory.Create(
        GetCurrentTimeUtc,
        name: "get_current_time_utc",
        description: "Returns the current UTC time as an ISO 8601 string. Use this when the user asks about the current time.");

    [Description("Returns the current UTC time as an ISO 8601 string.")]
    private static string GetCurrentTimeUtc()
        => DateTimeOffset.UtcNow.ToString("O");
}
