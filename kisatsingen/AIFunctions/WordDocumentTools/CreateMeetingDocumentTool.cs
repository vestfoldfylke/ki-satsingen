using System.ComponentModel;
using System.Text.Json;
using kisatsingen.Services.GeneratedFiles;
using kisatsingen.Services.MeetingDocuments;
using kisatsingen.Services.WordDocuments;
using Microsoft.Extensions.AI;
using static kisatsingen.AIFunctions.WordDocumentTools.WordDocumentToolResults;

namespace kisatsingen.AIFunctions.WordDocumentTools;

// Checks the meeting form and reports back; no file is made or kept here. The
// arguments are stored with the tool call in the chat, and the download button
// builds the file from them when it is clicked.
internal static class CreateMeetingDocumentTool
{
    public const string Name = "create_meeting_document";

    public const string Description =
        "Creates a Word file (.docx) on Vestfold fylkeskommune's template for meeting notices (møteinnkalling) and minutes (referat), and gives the user a download button. " +
        "Use it when the user asks for a meeting notice, an agenda for a meeting, or minutes. " +
        "The template is a form: fill the fields from the user's text, and leave out any field the text does not give; it stays blank for the user to fill in. " +
        "Never make up names, dates, decisions or deadlines. Write in the language of the user's text.";

    public const string Notice =
        "Møtedokumentet er laget. Brukeren laster det ned med knappen som vises ved dette verktøykallet. " +
        "Ikke skriv en lenke eller filsti til det; svar kort med hva dokumentet inneholder, og nevn felt som står tomme.";

    private const string KindDescription =
        "Notice: a meeting notice (møteinnkalling), sent before the meeting. Minutes: the minutes (referat), written after it. " +
        "The document's heading follows the kind.";

    // Built once: the factory reflects over the lambda and generates the schema
    // for the nested agenda array, and neither ever changes.
    public static readonly AIFunction Tool =
        ToolFunction.Create(Name, Description, (
            [Description("The document's title; it becomes the file name and the document title, e.g. \"Referat styremøte 24.11.2026\".")] string title,
            [Description(KindDescription)] MeetingDocumentKind kind,
            [Description("The meeting's name or purpose, e.g. \"Styremøte\".")] string? meeting = null,
            [Description("The date, as the user writes it, e.g. \"24.11.2026\".")] string? date = null,
            [Description("The time, e.g. \"10.00–11.30\".")] string? time = null,
            [Description("The place or video link.")] string? place = null,
            [Description("Who is invited, as a comma-separated list of names.")] string? invited = null,
            [Description("Who sent apologies (forfall), as a comma-separated list of names; usually only in minutes.")] string? absent = null,
            [Description("Who takes the minutes (referent).")] string? minuteTaker = null,
            [Description("The agenda items (Saksliste), in order; one row each. Leave it out when the text gives no agenda; the table stays blank.")] AgendaItemSpec[]? agendaItems = null) =>
            Check(new MeetingArguments(title, kind, meeting, agendaItems, date, time, place, invited, absent, minuteTaker)));

    // The arguments of a stored call, read the way the tool was given them. Null
    // when they are not a meeting document this build can make. Only what the
    // builder needs is checked, not the size limits: a call the tool accepted
    // keeps its download button when a limit is tightened later.
    public static MeetingDocumentSpec? ReadArguments(JsonElement arguments)
    {
        // Deserialize throws InvalidOperationException, not JsonException, on an
        // Undefined element, such as a call stored without arguments.
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var spec = arguments.Deserialize<MeetingDocumentSpec>(AIJsonUtilities.DefaultOptions);
            // A call made without agenda items stores none; it reads as an empty agenda.
            var read = spec is { AgendaItems: null } ? spec with { AgendaItems = [] } : spec;
            return read is { Title: not null }
                && MeetingDocumentSpecValidator.FindMissingAgendaItems(read.AgendaItems).Count == 0
                && MeetingDocumentSpecValidator.ValidateStructure(read).Count == 0
                    ? read
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The arguments are bound from the model's JSON, so any of them can be null
    // whatever the lambda's parameter types say.
    private sealed record MeetingArguments(
        string? Title, MeetingDocumentKind Kind, string? Meeting, AgendaItemSpec?[]? AgendaItems,
        string? Date, string? Time, string? Place, string? Invited, string? Absent, string? MinuteTaker);

    private static object Check(MeetingArguments arguments)
    {
        var missingItems = MeetingDocumentSpecValidator.FindMissingAgendaItems(arguments.AgendaItems ?? []);
        if (missingItems.Count > 0)
        {
            return Refuse(missingItems);
        }

        // OfType only narrows the type: no item is null past the check above.
        var spec = new MeetingDocumentSpec(
            arguments.Title ?? "", arguments.Kind, arguments.Meeting, [.. (arguments.AgendaItems ?? []).OfType<AgendaItemSpec>()],
            arguments.Date, arguments.Time, arguments.Place, arguments.Invited, arguments.Absent, arguments.MinuteTaker);

        var problems = MeetingDocumentSpecValidator.Validate(spec);
        if (problems.Count > 0)
        {
            return Refuse(problems);
        }

        var fileName = DownloadFileName.From(spec.Title, VestfoldWordDocumentBuilder.FileExtension, VestfoldMeetingDocumentBuilder.FallbackFileStem);
        return new MeetingDocumentCreatedResult(fileName, spec.AgendaItems.Count, Notice);
    }

    private static ToolError Refuse(IReadOnlyList<string> problems) =>
        new($"Møtedokumentet ble ikke laget. Rett dette og prøv igjen: {string.Join(" ", problems)}");
}
