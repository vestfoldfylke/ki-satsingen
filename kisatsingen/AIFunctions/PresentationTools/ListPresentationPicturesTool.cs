using kisatsingen.Services.Presentations.Pictures;
using Microsoft.Extensions.AI;
using static kisatsingen.AIFunctions.PresentationTools.PresentationToolResults;

namespace kisatsingen.AIFunctions.PresentationTools;

// The pictures create_presentation can use. A tool of its own rather than a
// list in create_presentation's description: tool definitions go with every
// request, and the list grows with the catalog, so only a turn that makes a
// deck should pay for it.
internal static class ListPresentationPicturesTool
{
    public const string Name = "list_presentation_pictures";

    public const string Description =
        "Lists the pictures create_presentation can put on picture slides, each with its pictureId and what it shows. " +
        "Call it before making a presentation that could use pictures.";

    public const string EmptyNotice = "Det finnes ingen bilder å bruke. Bruk bare lysbildetyper uten bilde.";

    public static readonly AIFunction Tool = ToolFunction.Create(Name, Description, () => List(PictureCatalog.Embedded));

    private static PresentationPicturesResult List(PictureCatalog pictureCatalog) =>
        new(
            [.. pictureCatalog.All.Select(picture => new PresentationPictureEntry(picture.Id, picture.Description))],
            pictureCatalog.All.Count == 0 ? EmptyNotice : null);
}
