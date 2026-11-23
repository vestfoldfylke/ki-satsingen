using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The simple template's body is only its usage guide and content controls;
// all of it goes, and only the section's properties (page size, margins,
// header and footer with the logo and page numbers) are kept.
internal static class SimpleLayout
{
    public static WordBodyLayout Prepare(Body body)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault()
            ?? throw new InvalidOperationException("The simple Word template's body has no section properties; the template file is damaged.");

        foreach (var element in body.ChildElements.Where(element => !ReferenceEquals(element, section)).ToList())
        {
            element.Remove();
        }

        return new WordBodyLayout(section, section);
    }
}
