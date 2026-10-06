using System.Globalization;

namespace kisatsingen.Services;

// Binary units, and the one way sizes are shown to users, so a limit in a
// refusal and a file in the panel read the same.
internal static class ByteSize
{
    public const long Kilobyte = 1024;
    public const long Megabyte = 1024 * Kilobyte;

    // In the caller's culture: shown on the circuit, which carries the app's
    // request culture (a decimal comma in nb-NO).
    public static string Format(long bytes) => bytes switch
    {
        < Kilobyte => $"{bytes} B",
        < Megabyte => string.Create(CultureInfo.CurrentCulture, $"{(double)bytes / Kilobyte:0} kB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{(double)bytes / Megabyte:0.#} MB")
    };
}
