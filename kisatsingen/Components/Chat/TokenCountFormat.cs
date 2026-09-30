namespace kisatsingen.Components.Chat;

// Shared so an estimate is marked the same everywhere.
internal static class TokenCountFormat
{
    public static string Format(long tokens) => tokens.ToString("N0");

    public static string Format(long tokens, bool isEstimated) => isEstimated ? $"~{Format(tokens)}" : Format(tokens);
}
