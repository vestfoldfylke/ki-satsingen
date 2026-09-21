using System.Runtime.CompilerServices;

namespace kisatsingen.Data.Repositories;

// One policy for user-supplied text on its way into a length-bounded column,
// shared by every repository so a blank chat title and a blank assistant name
// fail the same way instead of one falling back, one throwing and one silently
// doing nothing.
//
// Repositories reject rather than substitute. A caller that has a sensible
// default — ChatManager, which derives a title from the first message — applies
// it before calling, where the reason for the default is visible. A repository
// inventing one just hides the bug that produced the blank.
//
// Length is refused rather than truncated: silently shortening someone's title
// is a worse surprise than being told it is too long.
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

    // Blank becomes null: absent is a state worth representing exactly once, and
    // an empty string is the same thing spelled differently.
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
