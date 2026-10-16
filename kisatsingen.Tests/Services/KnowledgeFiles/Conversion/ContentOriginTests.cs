using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class ContentOriginTests
{
    [Fact]
    public void Every_content_origin_has_a_deliberate_note()
    {
        var origins = Enum.GetValues<ContentOrigin>();

        var exception = Record.Exception(() => origins.Select(origin => ContentOriginNotes.For(origin)).ToList());

        Assert.Null(exception);
    }
}
