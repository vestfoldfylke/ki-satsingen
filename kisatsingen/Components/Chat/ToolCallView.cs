using System.Text.Encodings.Web;
using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.Components.Presentation;
using kisatsingen.Services.Chat;
using static kisatsingen.AIFunctions.FileTools.FileToolResults;
using static kisatsingen.AIFunctions.PresentationTools.PresentationToolResults;

namespace kisatsingen.Components.Chat;

// Refusals are stored as Completed, since the tool returned a ToolError instead
// of throwing, but the UI shows them as Refused.
internal enum ToolOutcome
{
    Running,
    Succeeded,
    Refused,
    Failed,
    Interrupted
}

// A tool call as shown to the user. A result missing the fields a text needs,
// such as one stored in an older shape, falls back to a text without them.
internal sealed record ToolCallView(ToolCall ToolCall, ToolOutcome Outcome, string Title, string? UserFriendlyResult, GeneratedPresentation? Download)
{
    // Relaxed so æ, ø and å stay readable. Safe: Blazor HTML-encodes what it
    // renders, and this JSON never reaches a script.
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ToolCallView Build(ToolCall toolCall)
    {
        var result = ParseResult(toolCall.Result);
        var outcome = ClassifyOutcome(toolCall.Status, result);
        var (title, userFriendlyResult, download) = Describe(toolCall, outcome, result);
        return new ToolCallView(toolCall, outcome, title, userFriendlyResult, download);
    }

    // On demand rather than in Build: few open the raw data, so no copy is kept
    // unless it is shown. As stored, not interpreted: a refusal keeps its status
    // Completed here.
    public string FormatToolCallJson()
    {
        var result = ParseResult(ToolCall.Result);
        var document = new
        {
            tool = ToolCall.ToolName,
            status = ToolCall.Status,
            // Undefined when a stored tool call has no arguments at all.
            arguments = ToolCall.Arguments.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : ToolCall.Arguments,
            result = result is { } json ? (object)json : ToolCall.Result
        };
        return JsonSerializer.Serialize(document, IndentedJson);
    }

    private static ToolOutcome ClassifyOutcome(ToolStatus status, JsonElement? result) => status switch
    {
        ToolStatus.Running => ToolOutcome.Running,
        ToolStatus.Completed when result is { } json && ToolError.IsRefusal(json) => ToolOutcome.Refused,
        ToolStatus.Completed => ToolOutcome.Succeeded,
        ToolStatus.Failed => ToolOutcome.Failed,
        ToolStatus.Interrupted => ToolOutcome.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A tool status with no outcome.")
    };

    private sealed record ToolCallText(string Title, string? UserFriendlyResult = null, GeneratedPresentation? Download = null);

    private static ToolCallText Describe(ToolCall toolCall, ToolOutcome outcome, JsonElement? result)
    {
        if (outcome is ToolOutcome.Interrupted or ToolOutcome.Failed)
        {
            var runningTitle = Describe(toolCall, ToolOutcome.Running, result).Title;
            var stoppedBy = outcome is ToolOutcome.Interrupted ? "avbrutt" : "feilet";
            return new(Title: $"{runningTitle} ({stoppedBy})");
        }

        return toolCall.ToolName switch
        {
            ChatTools.GetCurrentTimeUtcToolName => DescribeCurrentTime(outcome, result),
            ListFilesTool.Name => DescribeListFiles(outcome, result),
            GetOutlineTool.Name => DescribeOutline(outcome, result),
            ReadFileTool.Name => DescribeReadFile(outcome, result),
            CreatePresentationTool.Name => DescribeCreatePresentation(outcome, result, toolCall.Arguments),
            _ => DescribeUnknownTool(toolCall.ToolName, outcome)
        };
    }

    private static ToolCallText DescribeCurrentTime(ToolOutcome outcome, JsonElement? result) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Sjekker hva klokka er"),
        ToolOutcome.Succeeded when result is { ValueKind: JsonValueKind.String } time => new(
            Title: "Sjekket hva klokka er",
            UserFriendlyResult: time.GetString()),
        ToolOutcome.Succeeded => new(Title: "Sjekket hva klokka er"),
        _ => new(Title: "Prøvde å sjekke hva klokka er")
    };

    private static ToolCallText DescribeListFiles(ToolOutcome outcome, JsonElement? result) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Ser hvilke filer som er tilgjengelige"),
        ToolOutcome.Succeeded when ReadAs<ListFilesResult>(result) is { Files: { } files } => new(
            Title: "Så hvilke filer som er tilgjengelige",
            UserFriendlyResult: files.Count == 0 ? "Ingen filer" : string.Join('\n', files.Select(file => file.FileName))),
        ToolOutcome.Succeeded => new(Title: "Så hvilke filer som er tilgjengelige"),
        _ => new(Title: "Prøvde å se hvilke filer som er tilgjengelige")
    };

    private static ToolCallText DescribeOutline(ToolOutcome outcome, JsonElement? result) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Lager innholdsfortegnelse"),
        ToolOutcome.Succeeded when ReadAs<GetOutlineResult>(result) is { FileName: { } fileName, Entries: { } entries } => new(
            Title: $"Laget innholdsfortegnelse for «{fileName}»",
            UserFriendlyResult: entries.Count == 0
                ? "Innholdsfortegnelsen er tom"
                : string.Join('\n', entries.Select(entry => $"{entry.Title} (linje {entry.StartLine} til {entry.EndLine})"))),
        ToolOutcome.Succeeded => new(Title: "Laget innholdsfortegnelse"),
        _ => new(Title: "Prøvde å lage innholdsfortegnelse")
    };

    private static ToolCallText DescribeReadFile(ToolOutcome outcome, JsonElement? result) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Leser filen"),
        ToolOutcome.Succeeded when ReadAs<ReadFileResult>(result) is { FileName: { } fileName, Content: { } content } read => new(
            Title: $"Leste «{fileName}», linje {read.StartLine} til {read.EndLine}",
            UserFriendlyResult: WithoutLineNumbers(content)),
        ToolOutcome.Succeeded => new(Title: "Leste filen"),
        _ => new(Title: "Prøvde å lese filen")
    };

    // The file is built from the arguments, which the tool accepted, so the
    // download needs nothing stored beyond the tool call itself.
    private static ToolCallText DescribeCreatePresentation(ToolOutcome outcome, JsonElement? result, JsonElement arguments) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Lager presentasjon"),
        ToolOutcome.Succeeded when ReadAs<PresentationCreatedResult>(result) is { FileName: { } fileName } created
            && CreatePresentationTool.ReadArguments(arguments) is { } spec => new(
                Title: $"Laget presentasjonen «{fileName}» ({created.SlideCount} lysbilder)",
                Download: new GeneratedPresentation(fileName, spec)),
        ToolOutcome.Succeeded => new(Title: "Laget presentasjon"),
        _ => new(Title: "Prøvde å lage presentasjon")
    };

    private static ToolCallText DescribeUnknownTool(string toolName, ToolOutcome outcome) => outcome switch
    {
        ToolOutcome.Running => new(Title: $"Bruker verktøyet {toolName}"),
        ToolOutcome.Succeeded => new(Title: $"Brukte verktøyet {toolName}"),
        _ => new(Title: $"Prøvde å bruke verktøyet {toolName}")
    };

    private static T? ReadAs<T>(JsonElement? result) where T : class =>
        result is { } json ? FileToolFunction.ReadResult<T>(json) : null;

    // Lines come as "12<tab>text"; the title already gives the range.
    private static string WithoutLineNumbers(string content) =>
        string.Join('\n', content.Split('\n').Select(line => line[(line.IndexOf('\t') + 1)..]));

    // Null unless the result is a JSON object, array or string.
    private static JsonElement? ParseResult(string? result)
    {
        if (result is null || result.AsSpan().TrimStart() is not ['{' or '[' or '"', ..])
        {
            return null;
        }

        try
        {
            return JsonElement.Parse(result);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
