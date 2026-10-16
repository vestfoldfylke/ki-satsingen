using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class DocumentConverterRegistryTests
{
    private sealed class StubConverter(string contentType) : IDocumentConverter
    {
        public IReadOnlyList<string> ContentTypes { get; } = [contentType];

        public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct) =>
            throw new NotSupportedException("The registry never converts.");
    }

    [Theory]
    [InlineData("text/plain", "text/plain")]
    [InlineData("text/plain", "Text/Plain")]
    public void Two_converters_for_one_type_are_refused_whatever_the_casing(string first, string second)
    {
        Assert.Throws<InvalidOperationException>(
            () => new DocumentConverterRegistry([new StubConverter(first), new StubConverter(second)]));
    }
}
