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

        var stem = TextTruncation.Prefix(
            string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' '),
            MaxStemLength)
            .TrimEnd('.', ' ');

        return stem.Length == 0 ? Fallback : stem + Extension;
    }
}
