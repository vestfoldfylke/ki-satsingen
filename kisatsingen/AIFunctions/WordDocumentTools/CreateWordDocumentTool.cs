using System.ComponentModel;
using System.Text.Json;
using kisatsingen.Services.GeneratedFiles;
using kisatsingen.Services.WordDocuments;
using Microsoft.Extensions.AI;
using static kisatsingen.AIFunctions.WordDocumentTools.WordDocumentToolResults;

namespace kisatsingen.AIFunctions.WordDocumentTools;

// Checks the blocks and reports back; no file is made or kept here. The
// arguments are stored with the tool call in the chat, and the download button
// builds the file from them when it is clicked.
internal static class CreateWordDocumentTool
{
    public const string Name = "create_word_document";

    public const string Description =
        "Creates a Word file (.docx) in Vestfold fylkeskommune's official design from content blocks you write, and gives the user a download button. " +
        "Use it when the user asks for a Word document, or for a document such as a memo (notat), letter, press release or report to download. " +
        "For a meeting notice (møteinnkalling) or minutes (referat), use " + CreateMeetingDocumentTool.Name + " instead. " +
        "Build the document from the user's own text: structure it and correct the language, but never add facts that are not in it. " +
        "With the Simple template, start with a Heading1 as the document's main heading, optionally after a Kicker with the date, followed by a short Lead. " +
        "With the Report template, the title, subtitle and date go on the cover, and the blocks start at the first chapter: use Heading2 for chapters, " +
        "Heading3 to Heading5 for subheadings, and FactBox for short fact boxes. " +
        "Word numbers the Report's Heading2 to Heading4 itself, so leave numbers such as \"1.\" or \"2.1\" out of the heading text, even when the user's text has them. " +
        "Use BulletList and NumberedList for lists instead of typing \"-\" or \"1.\" in a Paragraph, and PageBreak instead of empty paragraphs. " +
        "Keep tables simple: one header row and plain rows, no merged cells. Write in the language of the user's text.";

    public const string Notice =
        "Dokumentet er laget. Brukeren laster det ned med knappen som vises ved dette verktøykallet. " +
        "Ikke skriv en lenke eller filsti til det; svar kort med hva dokumentet inneholder.";

    // On the parameter rather than the enum members: the generated schema lists
    // an enum's names only, so member descriptions never reach the model.
    private const string TemplateDescription =
        "The template. Simple: for short documents over a few pages, such as a memo, a guide, a letter or a press release; no cover or table of contents. " +
        "Report: for longer documents that need a cover, a table of contents and several chapters; use it when the user asks for a report (rapport). " +
        "Use Simple unless the user asks for a report or the content clearly needs chapters.";

    // Built once: the factory reflects over the lambda and generates the schema
    // for the nested block array, and neither ever changes.
    public static readonly AIFunction Tool =
        ToolFunction.Create(Name, Description, (
            [Description("The document's title; it becomes the file name and the document title. In a Simple document it is not shown in the text, so usually repeat it as the Heading1; in a Report it is the cover title.")] string title,
            [Description(TemplateDescription)] WordDocumentTemplate template,
            [Description("The content blocks, in order.")] DocumentBlockSpec[] blocks,
            [Description("Report only: a short subtitle on the cover. Leave it out otherwise.")] string? subtitle = null,
            [Description("Report only: the date on the cover, as the user writes it, e.g. \"23.11.2026\". Leave it out when no date is given.")] string? date = null,
            [Description("Report only: a few sentences of extra information in the cover's text box. Leave it out otherwise.")] string? coverText = null) =>
            Check(title, template, blocks, subtitle, date, coverText));

    // The arguments of a stored call, read the way the tool was given them. Null
    // when they are not a document this build can make, such as one stored
    // before a block kind was removed. Only what the builder needs is checked,
    // not the size limits: a call the tool accepted keeps its download button
    // when a limit is tightened later.
    public static WordDocumentSpec? ReadArguments(JsonElement arguments)
    {
        // Deserialize throws InvalidOperationException, not JsonException, on an
        // Undefined element, such as a call stored without arguments.
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var spec = arguments.Deserialize<WordDocumentSpec>(AIJsonUtilities.DefaultOptions);
            return spec is { Title: not null, Blocks: not null }
                && WordDocumentSpecValidator.FindMissingBlocks(spec.Blocks).Count == 0
                && WordDocumentSpecValidator.ValidateStructure(spec).Count == 0
                    ? spec
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The arguments are bound from the model's JSON, so any of them can be null
    // whatever the lambda's parameter types say. A missing title or block list
    // is checked as an empty one, which the validator refuses with its reason.
    private static object Check(string? title, WordDocumentTemplate template, DocumentBlockSpec?[]? blocks, string? subtitle, string? date, string? coverText)
    {
        var missingBlocks = WordDocumentSpecValidator.FindMissingBlocks(blocks ?? []);
        if (missingBlocks.Count > 0)
        {
            return Refuse(missingBlocks);
        }

        // OfType only narrows the type: no block is null past the check above.
        var spec = new WordDocumentSpec(title ?? "", template, [.. (blocks ?? []).OfType<DocumentBlockSpec>()], subtitle, date, coverText);
        var problems = WordDocumentSpecValidator.Validate(spec);
        if (problems.Count > 0)
        {
            return Refuse(problems);
        }

        var fileName = DownloadFileName.From(spec.Title, VestfoldWordDocumentBuilder.FileExtension, VestfoldWordDocumentBuilder.FallbackFileStem);
        return new WordDocumentCreatedResult(fileName, spec.Blocks.Count, Notice);
    }

    private static ToolError Refuse(IReadOnlyList<string> problems) =>
        new($"Dokumentet ble ikke laget. Rett dette og prøv igjen: {string.Join(" ", problems)}");
}
