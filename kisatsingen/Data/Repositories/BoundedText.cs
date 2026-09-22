using System.Runtime.CompilerServices;

namespace kisatsingen.Data.Repositories;

// One policy for user-supplied text headed for a length-bounded column.
// Repositories reject rather than substitute — a caller with a sensible default
// (ChatManager, deriving a title from the first message) applies it before
// calling, where the reason is visible. Length is refused rather than
// truncated: silently shortening someone's title is the worse surprise.
internal static class BoundedText
{
    public static string RequireTrimmed(
        string value,
        string label,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{label} is blank. Supply a value, or apply a default before calling.", parameterName);
        }

        return WithinLimit(trimmed, label, maxLength, parameterName);
    }

    // Blank becomes null, so absence has one representation rather than two.
    public static string? TrimToNullable(
        string? value,
        string label,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : WithinLimit(trimmed, label, maxLength, parameterName);
    }

    private static string WithinLimit(string trimmed, string label, int maxLength, string? parameterName)
        => trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException(
                $"{label} is {trimmed.Length} characters, over the {maxLength} the column holds. Shorten it.",
                parameterName);
}
