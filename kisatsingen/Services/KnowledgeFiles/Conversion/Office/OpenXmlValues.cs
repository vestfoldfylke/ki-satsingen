using DocumentFormat.OpenXml;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office;

// Numbers from a file, read without throwing: .Value throws on a value the
// SDK can't parse, such as w:val="abc", so HasValue is checked first.
internal static class OpenXmlValues
{
    // Null when absent or not a number.
    public static int? ValueOf(Int32Value? number) => number is { HasValue: true }
        ? number.Value
        : null;

    // Null when absent or not a number.
    public static long? ValueOf(IntegerValue? number) => number is { HasValue: true }
        ? number.Value
        : null;
}
