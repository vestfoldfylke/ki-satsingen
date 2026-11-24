using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Fills the templates' content controls (the grey "[Tekst]" fields) and
// removes their guidance boxes.
internal static class ContentControls
{
    // The name the templates give their "Veiledning – slett denne boksen" shapes.
    private const string GuidanceShapeName = "instruksjon bilde";

    // The grey style a field's placeholder text is shown in.
    private const string PlaceholderStyle = "Plassholdertekst";

    // The XPath of a field bound to the document title, as Word writes it.
    private const string TitleBindingXPath = "/ns1:coreProperties[1]/ns0:title[1]";

    // Replaces an inline field with its text, keeping the field's run
    // formatting. The field goes: the text is the user's to edit freely.
    public static void ReplaceWithText(SdtRun field, string text)
    {
        var runProperties = field.SdtContentRun?.Descendants<RunProperties>().FirstOrDefault()?.CloneNode(deep: true)
            ?? field.SdtProperties?.GetFirstChild<RunProperties>()?.CloneNode(deep: true);

        var run = new Run();
        if (runProperties is RunProperties properties)
        {
            RemovePlaceholderStyle(properties);
            run.Append(properties);
        }

        WordText.AppendText(run, text);
        field.InsertAfterSelf(run);
        field.Remove();
    }

    // Fields bound to the title show the core title once Word refreshes them;
    // their cached text is set as well, for viewers that do not.
    public static void FillTitleFields(MainDocumentPart mainPart, string title)
    {
        OpenXmlPartRootElement?[] roots =
        [
            mainPart.Document,
            .. mainPart.HeaderParts.Select(part => part.Header),
            .. mainPart.FooterParts.Select(part => part.Footer)
        ];

        foreach (var field in roots.OfType<OpenXmlPartRootElement>().SelectMany(root => root.Descendants<SdtElement>()).Where(IsBoundToTitle).ToList())
        {
            FillBoundField(field, title);
        }
    }

    public static void RemoveGuidanceShapes(OpenXmlElement container)
    {
        var guidanceRuns = container.Descendants<Drawing.DocProperties>()
            .Where(properties => properties.Name?.Value == GuidanceShapeName)
            .Select(properties => properties.Ancestors<Run>().LastOrDefault())
            .OfType<Run>()
            .Distinct()
            .ToList();

        foreach (var run in guidanceRuns)
        {
            run.Remove();
        }
    }

    private static bool IsBoundToTitle(SdtElement field) =>
        field.SdtProperties?.GetFirstChild<DataBinding>()?.XPath?.Value == TitleBindingXPath;

    // The binding is kept, so the field follows the title if the user changes it.
    private static void FillBoundField(SdtElement field, string title)
    {
        field.SdtProperties?.GetFirstChild<ShowingPlaceholder>()?.Remove();

        var content = field.ChildElements.FirstOrDefault(child => child is SdtContentRun or SdtContentBlock);
        var firstRun = content?.Descendants<Run>().FirstOrDefault();
        if (content is null || firstRun is null)
        {
            return;
        }

        foreach (var run in content.Descendants<Run>().Skip(1).ToList())
        {
            run.Remove();
        }

        firstRun.RemoveAllChildren<Text>();
        if (firstRun.RunProperties is { } properties)
        {
            RemovePlaceholderStyle(properties);
        }

        firstRun.Append(new Text(title) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static void RemovePlaceholderStyle(RunProperties properties)
    {
        if (properties.RunStyle?.Val?.Value == PlaceholderStyle)
        {
            properties.RunStyle.Remove();
        }
    }
}
