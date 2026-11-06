using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

internal static class ConvertDocx
{
    private const int GenerousMaxCharacters = 1_000_000;
    private const int AnyMaxEstimatedTokens = 1;

    public static OfficeConversionResult Convert(byte[] docx, int maxCharacters = GenerousMaxCharacters, CancellationToken ct = default) =>
        DocxConverter.Convert(new MemoryStream(docx), new OfficeLimits(long.MaxValue, maxCharacters, AnyMaxEstimatedTokens), ct);

    public static string ToMarkdown(byte[] docx)
    {
        var result = Convert(docx);
        Assert.True(result.Success, $"The conversion failed: {result.ErrorType} {result.Exception}");
        return result.Markdown;
    }
}
