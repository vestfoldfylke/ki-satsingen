using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using static kisatsingen.AIFunctions.FileTools.FileToolResults;

namespace kisatsingen.Components.Chat;

// What a tool call did, in words for the user rather than the model. A result
// without the details a title uses, such as one stored in an older shape, gets
// the title without them.
internal static class ToolCallTitle
{
    public static string Describe(string toolName, ToolOutcome outcome, JsonElement? result) => toolName switch
    {
        ChatTools.GetCurrentTimeUtcToolName => ForOutcome(outcome,
            running: "Sjekker klokka",
            succeeded: "Sjekket klokka",
            unsuccessful: "Kunne ikke sjekke klokka"),
        ListFilesTool.Name => ForOutcome(outcome,
            running: "Sjekker hvilke filer som finnes",
            succeeded: "Sjekket hvilke filer som finnes",
            unsuccessful: "Kunne ikke sjekke hvilke filer som finnes"),
        GetOutlineTool.Name => ForOutcome(outcome,
            running: "Ser på innholdsoversikten",
            succeeded: ReadAs<GetOutlineResult>(result) is { FileName: { } fileName }
                ? $"Så på innholdsoversikten i «{fileName}»"
                : "Så på innholdsoversikten",
            unsuccessful: "Kunne ikke hente innholdsoversikten"),
        ReadFileTool.Name => ForOutcome(outcome,
            running: "Leser fil",
            succeeded: ReadAs<ReadFileResult>(result) is { FileName: { } fileName } read
                ? $"Leste «{fileName}», linje {read.StartLine} til {read.EndLine}"
                : "Leste filen",
            unsuccessful: "Kunne ikke lese filen"),
        _ => ForOutcome(outcome,
            running: $"Bruker verktøyet {toolName}",
            succeeded: $"Brukte verktøyet {toolName}",
            unsuccessful: $"Kunne ikke bruke verktøyet {toolName}")
    };

    private static string ForOutcome(ToolOutcome outcome, string running, string succeeded, string unsuccessful) => outcome switch
    {
        ToolOutcome.Running => running,
        ToolOutcome.Succeeded => succeeded,
        ToolOutcome.Refused or ToolOutcome.Failed => unsuccessful,
        ToolOutcome.Interrupted => "Avbrutt",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A tool outcome with no title.")
    };

    private static T? ReadAs<T>(JsonElement? result) where T : class =>
        result is { } json ? FileToolFunction.ReadResult<T>(json) : null;
}
