namespace kisatsingen.Tests;

// Characters a reader cannot see, or cannot tell apart, in source text, for
// tests that need them in their input. Each is built from its number and
// named after its Unicode name, so the test says which character it means,
// and no editor or tool can turn it into the raw, invisible character.
internal static class HardToSeeCharacters
{
    public static readonly string Null = char.ConvertFromUtf32(0x0000);
    public static readonly string Escape = char.ConvertFromUtf32(0x001B);

    public static readonly string ZeroWidthSpace = char.ConvertFromUtf32(0x200B);
    public static readonly string ZeroWidthJoiner = char.ConvertFromUtf32(0x200D);
    public static readonly string LineSeparator = char.ConvertFromUtf32(0x2028);
    public static readonly string RightToLeftOverride = char.ConvertFromUtf32(0x202E);
    public static readonly string ByteOrderMark = char.ConvertFromUtf32(0xFEFF);

    public static readonly string CombiningAcuteAccent = char.ConvertFromUtf32(0x0301);
    public static readonly string CombiningRingAbove = char.ConvertFromUtf32(0x030A);

    // Half of a surrogate pair is not a code point of its own, so it cannot go
    // through ConvertFromUtf32.
    public static readonly string LoneHighSurrogate = ((char)0xD83D).ToString();

    // Tag characters mirror ASCII at U+E0000 and up: invisible to a reader,
    // but read by a model.
    public static string AsTagCharacters(string asciiText) =>
        string.Concat(asciiText.Select(letter => char.ConvertFromUtf32(0xE0000 + letter)));
}
