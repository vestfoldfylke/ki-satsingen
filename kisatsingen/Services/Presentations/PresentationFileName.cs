using System.Text;

namespace kisatsingen.Services.Presentations;

// The download's file name, from the presentation title. Characters a file
// system or a Content-Disposition header could trip on become spaces.
internal static class PresentationFileName
{
    public const string Extension = ".pptx";
    public const string Fallback = "presentasjon" + Extension;
    public const int MaxStemLength = 100;

    private static readonly IReadOnlySet<char> InvalidCharacters =
        new HashSet<char>([.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*']);

    public static string From(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
        {
            builder.Append(char.IsControl(character) || InvalidCharacters.Contains(character) ? ' ' : character);
        }

        var stem = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (stem.Length > MaxStemLength)
        {
            // One shorter when the cut would split a surrogate pair.
            var length = char.IsHighSurrogate(stem[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength;
            stem = stem[..length].TrimEnd('.', ' ');
        }

        return stem.Length == 0 ? Fallback : stem + Extension;
    }
}
