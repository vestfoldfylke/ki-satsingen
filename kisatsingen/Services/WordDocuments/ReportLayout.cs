using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The report template's body is, in order: the cover, the table of contents,
// a section break, example content, and the back page. The cover, table of
// contents and back page are kept and filled; the example content goes, and
// the built content takes its place.
internal static class ReportLayout
{
    // The gallery names Word gives the building blocks the template is made of.
    private const string CoverPageGallery = "Cover Pages";
    private const string TableOfContentsGallery = "Table of Contents";

    public static WordBodyLayout Prepare(Body body, WordDocumentSpec spec)
    {
        var buildingBlocks = body.Elements<SdtBlock>().ToList();
        var cover = buildingBlocks.FirstOrDefault(block => GalleryOf(block) == CoverPageGallery)
            ?? throw TemplateChanged("no cover page");
        var tableOfContents = buildingBlocks.FirstOrDefault(block => GalleryOf(block) == TableOfContentsGallery)
            ?? throw TemplateChanged("no table of contents");
        var backPage = buildingBlocks.LastOrDefault(block => GalleryOf(block) == CoverPageGallery && !ReferenceEquals(block, cover))
            ?? throw TemplateChanged("no back page");
        var sectionBreak = tableOfContents.ElementsAfter().OfType<Paragraph>().FirstOrDefault(paragraph => paragraph.ParagraphProperties?.SectionProperties is not null)
            ?? throw TemplateChanged("no section break after the table of contents");
        var contentSection = body.Elements<SectionProperties>().LastOrDefault()
            ?? throw TemplateChanged("no section properties at the end of the body");

        RemoveBetween(sectionBreak, backPage);

        ReportCover.Fill(cover, spec);
        ReportTableOfContents.Reset(tableOfContents);
        ContentControls.RemoveGuidanceShapes(backPage);

        return new WordBodyLayout(backPage, contentSection);
    }

    private static void RemoveBetween(OpenXmlElement first, OpenXmlElement last)
    {
        foreach (var element in first.ElementsAfter().TakeWhile(element => !ReferenceEquals(element, last)).ToList())
        {
            element.Remove();
        }
    }

    private static string? GalleryOf(SdtBlock block) =>
        block.SdtProperties?.GetFirstChild<SdtContentDocPartObject>()?.GetFirstChild<DocPartGallery>()?.Val?.Value;

    private static InvalidOperationException TemplateChanged(string what) =>
        new($"The report Word template has {what}. The template changed; update {nameof(ReportLayout)} to match it.");
}
