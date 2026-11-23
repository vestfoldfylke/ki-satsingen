using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Strips what the template carries for its own use, so only built content
// and the template's design remain.
internal static class WordTemplateCleanup
{
    // The template's link to itself on a SharePoint or file path; left in, Word
    // tries to reach that path whenever the document opens.
    public static void RemoveAttachedTemplate(MainDocumentPart mainPart)
    {
        if (mainPart.DocumentSettingsPart is not { } settingsPart
            || settingsPart.Settings?.GetFirstChild<AttachedTemplate>() is not { } attachedTemplate)
        {
            return;
        }

        var relationshipId = attachedTemplate.Id?.Value;
        attachedTemplate.Remove();

        if (settingsPart.ExternalRelationships.FirstOrDefault(relationship => relationship.Id == relationshipId) is { } templateRelationship)
        {
            settingsPart.DeleteExternalRelationship(templateRelationship);
        }
    }

    // SharePoint's content type and document library metadata, and the
    // template's own properties: none belong to a new document.
    public static void RemoveTemplateMetadata(WordprocessingDocument document)
    {
        if (document.CustomFilePropertiesPart is { } customProperties)
        {
            document.DeletePart(customProperties);
        }

        if (document.MainDocumentPart is { } mainPart)
        {
            mainPart.DeleteParts(mainPart.CustomXmlParts.ToList());
        }

        // app.xml and the thumbnail are optional; Word writes both anew on its first save.
        if (document.ExtendedFilePropertiesPart is { } extendedProperties)
        {
            document.DeletePart(extendedProperties);
        }

        if (document.ThumbnailPart is { } thumbnail)
        {
            document.DeletePart(thumbnail);
        }
    }
}
