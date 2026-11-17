using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace kisatsingen.Services.Presentations;

// Strips the template's own slides — its usage guide — so only built slides remain.
internal static class TemplateCleanup
{
    // The PowerPoint 2010 extension holding the slide sections (p14:sectionLst).
    private const string SectionListExtensionUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    public static void RemoveTemplateSlides(PresentationPart presentationPart)
    {
        var presentation = presentationPart.Presentation
            ?? throw new InvalidOperationException("The presentation template has no presentation.xml content; the template file is damaged.");

        foreach (var slideId in presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [])
        {
            var relationshipId = slideId.RelationshipId?.Value;
            slideId.Remove();

            if (relationshipId is not null)
            {
                presentationPart.DeletePart(relationshipId);
            }
        }

        RemoveSlideReferences(presentation);
    }

    // Sections and custom shows list slides by id. Left behind, they point at
    // slides that no longer exist, and PowerPoint offers to repair the file.
    private static void RemoveSlideReferences(Presentation presentation)
    {
        presentation.CustomShowList?.Remove();

        var extensions = presentation.PresentationExtensionList;
        if (extensions is null)
        {
            return;
        }

        foreach (var sections in extensions.Elements<PresentationExtension>().Where(e => e.Uri?.Value == SectionListExtensionUri).ToList())
        {
            sections.Remove();
        }

        if (!extensions.HasChildren)
        {
            extensions.Remove();
        }
    }
}
