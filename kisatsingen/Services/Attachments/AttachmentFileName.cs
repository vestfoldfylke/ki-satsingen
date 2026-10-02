using System.Text;
using kisatsingen.Data.Entities;

namespace kisatsingen.Services.Attachments;

// For display and for the stored file name only; it never reaches the file
// system (see TempFileStore). It does reach the model, in the attachment line.
public static class AttachmentFileName
{
    private const string FallbackName = "fil";

    // What a file name needs besides letters and digits: the ordinary space
    // and a little punctuation. Anything else, visible or not, is dropped: an
    // allowlist leaves nothing that could disguise a name or break the line it
    // is shown in, including characters no one thought of.
    private const string AllowedSpaceAndPunctuation = " .-_(),";

    public static string Normalize(string clientFileName)
    {
        // Browsers send a base name, but the value is the client's to choose.
        // By code point, not by char, which also turns a lone surrogate into
        // U+FFFD, so the text is well formed before it is composed.
        var wellFormed = string.Concat(Path.GetFileName(clientFileName).EnumerateRunes().Select(rune => rune.ToString()));

        // Composed before filtering, so "å" sent as "a" and a combining ring
        // (as macOS can) keeps its ring instead of losing it as a mark.
        var composed = wellFormed.Normalize(NormalizationForm.FormC);

        var name = string.Concat(composed.EnumerateRunes().Where(IsAllowedInName).Select(rune => rune.ToString())).Trim();
        if (name.Length == 0)
        {
            return FallbackName;
        }

        if (name.Length <= KnowledgeFile.MaxFileNameLength)
        {
            return name;
        }

        // The extension decides the type, so it survives the cut.
        var extension = Path.GetExtension(name);
        return extension.Length >= KnowledgeFile.MaxFileNameLength
            ? TextTruncation.Prefix(name, KnowledgeFile.MaxFileNameLength)
            : TextTruncation.Prefix(name, KnowledgeFile.MaxFileNameLength - extension.Length) + extension;
    }

    private static bool IsAllowedInName(Rune rune) =>
        Rune.IsLetterOrDigit(rune) || (rune.IsAscii && AllowedSpaceAndPunctuation.Contains((char)rune.Value));
}
