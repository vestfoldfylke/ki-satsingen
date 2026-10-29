using System.Globalization;

namespace kisatsingen.Components.Chat;

// Shared so an estimate is marked the same everywhere.
internal static class TokenCountFormat
{
    // In the caller's culture: shown on the circuit, which carries the app's
    // request culture (a non-breaking space between thousands in nb-NO).
    public static string Format(long tokens) => tokens.ToString("N0", CultureInfo.CurrentCulture);

    public static string Format(long tokens, bool isEstimated) => isEstimated ? $"~{Format(tokens)}" : Format(tokens);
}
