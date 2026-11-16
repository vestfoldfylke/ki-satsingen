using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace kisatsingen.Services.Presentations;

// Turns a validated spec into a .pptx built on the Vestfold template. The same
// spec gives the same slides; only the file's created time differs.
public sealed class VestfoldPresentationBuilder(PresentationTemplate template, TimeProvider timeProvider)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    // PowerPoint numbers slide ids from 256; lower ids are reserved.
    private const uint FirstSlideId = 256;

    // The stream's own buffer, not a copy of it: a deck is LOH-sized, and the
    // caller only reads it.
    public ArraySegment<byte> Build(PresentationSpec spec)
    {
        using var stream = template.OpenWritableCopy();

        using (var document = PresentationDocument.Open(stream, isEditable: true))
        {
            // The template's main part has the template content type; a .pptx
            // with it opens as a new, untitled presentation, or as damaged.
            document.ChangeDocumentType(PresentationDocumentType.Presentation);

            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("The presentation template has no presentation part; the template file is damaged.");

            TemplateCleanup.RemoveTemplateSlides(presentationPart);
            AddSlides(presentationPart, spec);

            SetDocumentProperties(document, spec.Title);
        }

        return stream.TryGetBuffer(out var content)
            ? content
            : throw new InvalidOperationException("The template copy's buffer is not exposable; PresentationTemplate must open it as an expandable MemoryStream.");
    }

    // The template's own properties would otherwise be copied into every file:
    // its 2024 dates, its slide count and slide titles in app.xml, and a
    // thumbnail of its first slide. app.xml and the thumbnail are optional
    // parts, and PowerPoint writes both anew on its first save.
    private void SetDocumentProperties(PresentationDocument document, string title)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        document.PackageProperties.Title = title;
        document.PackageProperties.Created = now;
        document.PackageProperties.Modified = now;

        if (document.ExtendedFilePropertiesPart is { } extendedProperties)
        {
            document.DeletePart(extendedProperties);
        }

        if (document.ThumbnailPart is { } thumbnail)
        {
            document.DeletePart(thumbnail);
        }
    }

    private static void AddSlides(PresentationPart presentationPart, PresentationSpec spec)
    {
        var master = FindMaster(presentationPart, spec.Theme);
        var presentation = presentationPart.Presentation!;
        var slideIdList = presentation.SlideIdList ?? presentation.AppendChild(new SlideIdList());

        var slideId = FirstSlideId;
        foreach (var slide in spec.Slides)
        {
            var layoutPart = FindLayout(master, spec.Theme, SlideLayouts.For(slide.Kind).LayoutName);
            var slidePart = SlideFactory.Create(presentationPart, layoutPart, slide);
            slideIdList.Append(new SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }
    }

    // The masters are listed in theme order; see PresentationTheme.
    private static SlideMasterPart FindMaster(PresentationPart presentationPart, PresentationTheme theme)
    {
        var masterIds = presentationPart.Presentation?.SlideMasterIdList?.Elements<SlideMasterId>().ToList() ?? [];
        var index = (int)theme;

        if (index >= masterIds.Count || masterIds[index].RelationshipId?.Value is not { } relationshipId)
        {
            throw new InvalidOperationException(
                $"The template has {masterIds.Count} slide masters, so theme {theme} (master {index + 1}) is missing. The template changed; update {nameof(PresentationTheme)} to match it.");
        }

        return (SlideMasterPart)presentationPart.GetPartById(relationshipId);
    }

    private static SlideLayoutPart FindLayout(SlideMasterPart master, PresentationTheme theme, string layoutName) =>
        master.SlideLayoutParts.FirstOrDefault(part => part.SlideLayout?.CommonSlideData?.Name?.Value?.Trim() == layoutName)
        ?? throw new InvalidOperationException(
            $"Theme {theme} has no layout named \"{layoutName}\". The template changed; update {nameof(SlideLayouts)} to match it.");
}
