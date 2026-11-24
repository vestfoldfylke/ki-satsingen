using System.Text.Encodings.Web;
using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.FileTools;
using kisatsingen.AIFunctions.PresentationTools;
using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.AIFunctions.WordDocumentTools;
using kisatsingen.Services.Chat;
using kisatsingen.Services.GeneratedFiles;
using static kisatsingen.AIFunctions.FileTools.FileToolResults;
using static kisatsingen.AIFunctions.PresentationTools.PresentationToolResults;
using static kisatsingen.AIFunctions.WebSearch.SearchWebResults;
using static kisatsingen.AIFunctions.WordDocumentTools.WordDocumentToolResults;

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
internal sealed record ToolCallView(ToolCall ToolCall, ToolOutcome Outcome, string Title, string? UserFriendlyResult, GeneratedFile? Download)
{
    // Relaxed so æ, ø and å stay readable. Safe: Blazor HTML-encodes what it
    // renders, and this JSON never reaches a script.
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string SearchQueryArgument = "query";

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

    private sealed record ToolCallText(string Title, string? UserFriendlyResult = null, GeneratedFile? Download = null);

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
            ListFilesTool.Name => DescribeListFiles(outcome, result),
            GetOutlineTool.Name => DescribeOutline(outcome, result),
            ReadFileTool.Name => DescribeReadFile(outcome, result),
            CreatePresentationTool.Name => DescribeCreatePresentation(outcome, result, toolCall.Arguments),
            ListPresentationPicturesTool.Name => DescribeListPresentationPictures(outcome, result),
            CreateWordDocumentTool.Name => DescribeCreateWordDocument(outcome, result, toolCall.Arguments),
            CreateMeetingDocumentTool.Name => DescribeCreateMeetingDocument(outcome, result, toolCall.Arguments),
            SearchWebTool.Name => DescribeSearchWeb(outcome, result, toolCall.Arguments),
            _ => DescribeUnknownTool(toolCall.ToolName, outcome)
        };
    }

    // Titled from the query the chat model asked, which is known while the
    // search runs. The body shows what the provider searched for, which can
    // differ, and the sources it cited, so the user sees what the answer is
    // based on.
    private static ToolCallText DescribeSearchWeb(ToolOutcome outcome, JsonElement? result, JsonElement arguments)
    {
        var searchedFor = DescribeSearchedFor(arguments);
        var refusal = ReadAs<ToolError>(result)?.Error;
        return outcome switch
        {
            ToolOutcome.Running => new(Title: $"Søker {searchedFor}"),
            ToolOutcome.Succeeded when ReadAs<SearchWebResult>(result) is { Queries: { Count: > 0 } queries, Sources: { } sources } => new(
                Title: $"Søkte {searchedFor}",
                UserFriendlyResult: $"Søkte etter:\n{string.Join('\n', queries)}\n\nKilder:\n" + (sources.Count == 0
                    ? "Ingen"
                    : string.Join('\n', sources.Select(source => source.Title is { } title ? $"{title} – {source.Url}" : source.Url)))),
            ToolOutcome.Succeeded => new(Title: $"Søkte {searchedFor}"),
            ToolOutcome.Refused when refusal == SearchWebTexts.SearchIsOff => new(Title: "Søkte ikke – nettsøk er av"),
            _ => new(Title: $"Prøvde å søke {searchedFor}", UserFriendlyResult: DescribeSearchRefusal(refusal))
        };
    }

    private static string DescribeSearchedFor(JsonElement arguments)
    {
        var query = arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(SearchQueryArgument, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var sites = words.Where(IsSite).Select(word => word[SearchWebTool.SitePrefix.Length..]).ToList();
        var keywords = string.Join(' ', words.Where(word => !IsSite(word)));

        var where = sites.Count == 0 ? "på nettet" : $"på {string.Join(" og ", sites)}";
        return keywords.Length == 0 ? where : $"etter «{keywords}» {where}";
    }

    private static bool IsSite(string word) =>
        word.Length > SearchWebTool.SitePrefix.Length && word.StartsWith(SearchWebTool.SitePrefix, StringComparison.OrdinalIgnoreCase);

    // The refusals are written for the model, so each is told to the user in
    // words of its own. A message not known here, such as one stored before a
    // text changed, shows no reason, and the raw data still has it.
    private static string? DescribeSearchRefusal(string? refusal) =>
        refusal switch
        {
            SearchWebTexts.EmptyQuery => "Søket var tomt.",
            SearchWebTexts.WebAddressRefused => "Søket kan ikke inneholde nettadresser.",
            SearchWebTexts.NationalIdentityNumberRefused => "Søket kan ikke inneholde fødselsnummer.",
            SearchWebTexts.NotAvailable => "Nettsøket var ikke tilgjengelig.",
            SearchWebTexts.NoResult => "Søket ga ingen treff.",
            { } error when error == SearchWebTexts.LimitReached(WebSearchQuota.MaxSearchesPerTurn) =>
                $"Det kan bare søkes {WebSearchQuota.MaxSearchesPerTurn} ganger per svar.",
            _ => null
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
            Title: ReadTitle(fileName, read, content),
            UserFriendlyResult: WithoutLineNumbers(content)),
        ToolOutcome.Succeeded when ReadAs<ReadFileTooLargeResult>(result) is { FileName: { } fileName, EstimatedTokens: > 0 } => new(
            Title: $"«{fileName}» er for stor til å leses på én gang"),
        ToolOutcome.Succeeded => new(Title: "Leste filen"),
        _ => new(Title: "Prøvde å lese filen")
    };

    // Titled from what was read, not what was asked, so "hele" is only claimed
    // when every line came back uncut.
    private static string ReadTitle(string fileName, ReadFileResult read, string content)
    {
        var hasCutLines = content.Contains(FileToolTexts.LineCutMarkerStart, StringComparison.Ordinal);
        if (read.StartLine == 1 && read.EndLine == read.LineCount && !hasCutLines)
        {
            return $"Leste hele «{fileName}»";
        }

        var cutNote = hasCutLines ? " (lange linjer er kuttet)" : "";
        return $"Leste «{fileName}», linje {read.StartLine} til {read.EndLine} av {read.LineCount}{cutNote}";
    }

    // The file is built from the arguments, which the tool accepted, so the
    // download needs nothing stored beyond the tool call itself.
    private static ToolCallText DescribeCreatePresentation(ToolOutcome outcome, JsonElement? result, JsonElement arguments) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Lager presentasjon"),
        ToolOutcome.Succeeded when ReadAs<PresentationCreatedResult>(result) is { FileName: { } fileName } created
            && CreatePresentationTool.ReadArguments(arguments) is { } spec => new(
                Title: $"Laget presentasjonen «{fileName}» ({created.SlideCount} lysbilder)",
                Download: new GeneratedFile.Presentation(fileName, spec)),
        ToolOutcome.Succeeded => new(Title: "Laget presentasjon"),
        _ => new(Title: "Prøvde å lage presentasjon")
    };

    // Built from the arguments like a presentation; see DescribeCreatePresentation.
    private static ToolCallText DescribeCreateWordDocument(ToolOutcome outcome, JsonElement? result, JsonElement arguments) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Lager Word-dokument"),
        ToolOutcome.Succeeded when ReadAs<WordDocumentCreatedResult>(result) is { FileName: { } fileName }
            && CreateWordDocumentTool.ReadArguments(arguments) is { } spec => new(
                Title: $"Laget Word-dokumentet «{fileName}»",
                Download: new GeneratedFile.WordDocument(fileName, spec)),
        ToolOutcome.Succeeded => new(Title: "Laget Word-dokument"),
        _ => new(Title: "Prøvde å lage Word-dokument")
    };

    private static ToolCallText DescribeCreateMeetingDocument(ToolOutcome outcome, JsonElement? result, JsonElement arguments) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Lager møtedokument"),
        ToolOutcome.Succeeded when ReadAs<MeetingDocumentCreatedResult>(result) is { FileName: { } fileName }
            && CreateMeetingDocumentTool.ReadArguments(arguments) is { } spec => new(
                Title: $"Laget møtedokumentet «{fileName}»",
                Download: new GeneratedFile.MeetingDocument(fileName, spec)),
        ToolOutcome.Succeeded => new(Title: "Laget møtedokument"),
        _ => new(Title: "Prøvde å lage møtedokument")
    };

    private static ToolCallText DescribeListPresentationPictures(ToolOutcome outcome, JsonElement? result) => outcome switch
    {
        ToolOutcome.Running => new(Title: "Ser hvilke bilder presentasjonen kan bruke"),
        ToolOutcome.Succeeded when ReadAs<PresentationPicturesResult>(result) is { Pictures: { } pictures } => new(
            Title: $"Så hvilke bilder presentasjonen kan bruke ({pictures.Count} bilder)"),
        ToolOutcome.Succeeded => new(Title: "Så hvilke bilder presentasjonen kan bruke"),
        _ => new(Title: "Prøvde å se hvilke bilder presentasjonen kan bruke")
    };

    private static ToolCallText DescribeUnknownTool(string toolName, ToolOutcome outcome) => outcome switch
    {
        ToolOutcome.Running => new(Title: $"Bruker verktøyet {toolName}"),
        ToolOutcome.Succeeded => new(Title: $"Brukte verktøyet {toolName}"),
        _ => new(Title: $"Prøvde å bruke verktøyet {toolName}")
    };

    private static T? ReadAs<T>(JsonElement? result) where T : class =>
        result is { } json ? ToolFunction.ReadResult<T>(json) : null;

    // The line numbers are for the model; the title tells the user which lines were read.
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
