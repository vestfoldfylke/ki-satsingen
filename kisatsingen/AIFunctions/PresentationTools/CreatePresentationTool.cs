using System.ComponentModel;
using System.Text.Json;
using kisatsingen.Services.GeneratedFiles;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using Microsoft.Extensions.AI;
using static kisatsingen.AIFunctions.PresentationTools.PresentationToolResults;

namespace kisatsingen.AIFunctions.PresentationTools;

// Checks the slides and reports back; no file is made or kept here. The
// arguments are stored with the tool call in the chat, and the download button
// builds the file from them when it is clicked.
internal static class CreatePresentationTool
{
    public const string Name = "create_presentation";

    public const string Description =
        "Creates a PowerPoint file (.pptx) in Vestfold fylkeskommune's official design from slides you write, and gives the user a download button. " +
        "Use it when the user asks for a presentation, slides or a PowerPoint, typically from text they paste. " +
        "Build the slides from the user's own text: shorten and structure it, but never add facts that are not in it. " +
        "Keep bullets to short phrases; prefer more slides over crowded ones. " +
        "Unless the user asks for something else, start with a Cover or CoverWithPicture slide; add an Agenda and Chapter dividers for longer material; end with a Closing slide when it fits. " +
        "Every slide needs a title. Write in the language of the user's text. " +
        "Picture slides (the kinds with Picture in the name) take a pictureId: call " +
        ListPresentationPicturesTool.Name + " first to get the ids, and use picture slides only where a listed picture clearly fits the content. Never make up an id. " +
        "Unless the user says where or how big the pictures should be, use CoverWithPicture for the cover and ContentWithSmallPicture for content, " +
        "and follow the user's wishes when they ask for something else. " +
        "If the user says they will add pictures themselves, use picture slides without a pictureId; each gets an empty picture frame.";

    public const string Notice =
        "Presentasjonen er laget. Brukeren laster den ned med knappen som vises ved dette verktøykallet. " +
        "Ikke skriv en lenke eller filsti til den; svar kort med hva presentasjonen inneholder.";

    // On the parameter rather than the enum members: the generated schema lists
    // an enum's names only, so member descriptions never reach the model.
    private const string ThemeDescription =
        "The colour theme: SivStrand (sand and beige), VannFjord (blue), BarkNype (warm bark and rose) or GranKorn (green and grain). " +
        "Use SivStrand unless the user asks for another theme or for one of those colours.";

    // Built once: the factory reflects over the lambda and generates the schema
    // for the nested slide array, and neither ever changes.
    public static readonly AIFunction Tool =
        ToolFunction.Create(Name, Description, (
            [Description("The presentation's title; it also becomes the file name and the document title.")] string title,
            [Description(ThemeDescription)] PresentationTheme theme,
            [Description("The slides, in order.")] SlideSpec[] slides) => Check(title, theme, slides));

    // The arguments of a stored call, read the way the tool was given them. Null
    // when they are not a presentation this build can make, such as one stored
    // before a slide kind was removed. Only what the builder needs is checked,
    // not the size limits: a call the tool accepted keeps its download button
    // when a limit is tightened later.
    public static PresentationSpec? ReadArguments(JsonElement arguments)
    {
        // Deserialize throws InvalidOperationException, not JsonException, on an
        // Undefined element, such as a call stored without arguments.
        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var spec = arguments.Deserialize<PresentationSpec>(AIJsonUtilities.DefaultOptions);
            return spec is { Title: not null, Slides: not null }
                && PresentationSpecValidator.FindMissingSlides(spec.Slides).Count == 0
                && PresentationSpecValidator.ValidateStructure(spec).Count == 0
                    ? spec
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The arguments are bound from the model's JSON, so any of them can be null
    // whatever the lambda's parameter types say. A missing title or slide list
    // is checked as an empty one, which the validator refuses with its reason.
    private static object Check(string? title, PresentationTheme theme, SlideSpec?[]? slides)
    {
        var missingSlides = PresentationSpecValidator.FindMissingSlides(slides ?? []);
        if (missingSlides.Count > 0)
        {
            return Refuse(missingSlides);
        }

        // OfType only narrows the type: no slide is null past the check above.
        var spec = new PresentationSpec(title ?? "", theme, [.. (slides ?? []).OfType<SlideSpec>()]);
        var problems = PresentationSpecValidator.Validate(spec, PictureCatalog.Embedded);
        if (problems.Count > 0)
        {
            return Refuse(problems);
        }

        return new PresentationCreatedResult(DownloadFileName.From(spec.Title, VestfoldPresentationBuilder.FileExtension, VestfoldPresentationBuilder.FallbackFileStem), spec.Slides.Count, Notice);
    }

    private static ToolError Refuse(IReadOnlyList<string> problems) =>
        new($"Presentasjonen ble ikke laget. Rett dette og prøv igjen: {string.Join(" ", problems)}");
}
