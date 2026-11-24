using System.Text;

namespace kisatsingen.Services.GeneratedFiles;

// A download's file name, from the title the model gave the file. Characters a
// file system or a Content-Disposition header could trip on become spaces.
internal static class DownloadFileName
{
    public const int MaxStemLength = 100;

    private static readonly IReadOnlySet<char> InvalidCharacters =
        new HashSet<char>([.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*']);

    // The fallback stem is used when nothing of the title is left.
    public static string From(string title, string extension, string fallbackStem)
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

        return (stem.Length == 0 ? fallbackStem : stem) + extension;
    }
}
