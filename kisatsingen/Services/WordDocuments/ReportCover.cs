using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Fills the report's cover: the title (a field bound to the document title,
// filled with the others in ContentControls), the date, the subtitle and the
// text box. The photo credit is emptied: the picture frame stays empty, which
// this cover allows. Fields not given are removed, as the template asks.
internal static class ReportCover
{
    private const string TextBoxTitleStyle = "forside--tekstbokstittel";
    private const string TextBoxStyle = "forside--tekstboks";
    private const string PhotoCreditStyle = "forside--bildetekst";

    public static void Fill(SdtBlock cover, WordDocumentSpec spec)
    {
        ContentControls.RemoveGuidanceShapes(cover);
        FillDate(cover, spec.Date);
        FillSubtitle(cover, spec.Subtitle);

        // The text box and credit are in a drawing Word stores twice, once for
        // itself and once for older readers, so every match is filled.
        foreach (var paragraph in ParagraphsStyled(cover, TextBoxTitleStyle).Concat(ParagraphsStyled(cover, PhotoCreditStyle)))
        {
            Empty(paragraph);
        }

        foreach (var paragraph in ParagraphsStyled(cover, TextBoxStyle))
        {
            FillWith(paragraph, spec.CoverText);
        }
    }

    private static void FillDate(SdtBlock cover, string? date)
    {
        foreach (var field in cover.Descendants<SdtRun>().Where(field => field.SdtProperties?.GetFirstChild<SdtContentDate>() is not null).ToList())
        {
            if (date is null)
            {
                field.Remove();
                continue;
            }

            ContentControls.ReplaceWithText(field, date);
        }
    }

    private static void FillSubtitle(SdtBlock cover, string? subtitle)
    {
        foreach (var paragraph in ParagraphsStyled(cover, WordStyles.Subtitle))
        {
            FillWith(paragraph, subtitle);
            if (subtitle is null)
            {
                paragraph.Remove();
            }
        }
    }

    // The paragraph's first field takes the text; anything else in it goes.
    private static void FillWith(Paragraph paragraph, string? text)
    {
        if (text is null || paragraph.Elements<SdtRun>().FirstOrDefault() is not { } field)
        {
            Empty(paragraph);
            return;
        }

        foreach (var other in paragraph.ChildElements.Where(child => child is not ParagraphProperties && !ReferenceEquals(child, field)).ToList())
        {
            other.Remove();
        }

        ContentControls.ReplaceWithText(field, text);
    }

    private static void Empty(OpenXmlElement paragraph)
    {
        foreach (var child in paragraph.ChildElements.Where(child => child is not ParagraphProperties).ToList())
        {
            child.Remove();
        }
    }

    private static List<Paragraph> ParagraphsStyled(OpenXmlElement container, string styleId) =>
        [.. container.Descendants<Paragraph>().Where(paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == styleId)];
}
