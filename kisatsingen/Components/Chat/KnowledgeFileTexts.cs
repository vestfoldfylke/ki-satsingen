using System.Diagnostics;
using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Components.Chat;

// What the files panel says about a knowledge file. The model's wording lives
// in FileToolTexts; the two may diverge.
internal static class KnowledgeFileTexts
{
    // How far to trust the text. Exact text gets no note. An origin this build
    // does not know gets the most cautious one.
    public static string? OriginNote(ContentOrigin? origin) => origin switch
    {
        ContentOrigin.TextFile => null,
        ContentOrigin.ImagePlaceholder => "Innholdet i bildet er ikke tolket.",
        null => "Det er ukjent hvordan teksten ble hentet ut.",
        _ => throw new UnreachableException($"ContentOrigin {origin} has no note for the files panel. Add one here in the same change that adds the value.")
    };
}
