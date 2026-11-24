using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace kisatsingen.Services.WordDocuments;

// The steps every Word build shares: open a copy of the template as a
// document, strip what belongs to the template, let the caller fill the body,
// and title and date the file.
internal static class WordTemplateBuild
{
    // The stream's own buffer, not a copy of it: the caller only reads it.
    public static ArraySegment<byte> Run(MemoryStream templateCopy, string title, DateTime now, Action<MainDocumentPart> fill)
    {
        using (templateCopy)
        {
            using (var document = WordprocessingDocument.Open(templateCopy, isEditable: true))
            {
                // The template's main part has the template content type; a .docx
                // with it opens as a new, untitled document, or as damaged.
                document.ChangeDocumentType(WordprocessingDocumentType.Document);

                var mainPart = document.MainDocumentPart
                    ?? throw new InvalidOperationException("The Word template has no main document part; the template file is damaged.");

                WordTemplateCleanup.RemoveAttachedTemplate(mainPart);
                WordTemplateCleanup.RemoveTemplateMetadata(document);

                fill(mainPart);

                document.PackageProperties.Title = title;
                document.PackageProperties.Created = now;
                document.PackageProperties.Modified = now;
            }

            return templateCopy.TryGetBuffer(out var content)
                ? content
                : throw new InvalidOperationException("The template copy's buffer is not exposable; WordTemplateFiles must open it as an expandable MemoryStream.");
        }
    }
}
