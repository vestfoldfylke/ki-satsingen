using kisatsingen.Components.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Components.Chat;

public sealed class KnowledgeFileTextsTests
{
    // A missing note throws, which would break the files panel as it renders.
    [Fact]
    public void Every_content_origin_has_a_deliberate_note_for_the_files_panel()
    {
        var origins = Enum.GetValues<ContentOrigin>();

        var exception = Record.Exception(() => origins.Select(origin => KnowledgeFileTexts.OriginNote(origin)).ToList());

        Assert.Null(exception);
    }
}
