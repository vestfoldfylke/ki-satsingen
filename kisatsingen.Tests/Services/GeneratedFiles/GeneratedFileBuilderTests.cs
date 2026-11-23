using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using kisatsingen.Services.GeneratedFiles;
using kisatsingen.Services.MeetingDocuments;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.Presentations.Pictures;
using kisatsingen.Services.WordDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.GeneratedFiles;

public sealed class GeneratedFileBuilderTests
{
    private static readonly GeneratedFile Presentation = new GeneratedFile.Presentation(
        "Budsjett.pptx", new PresentationSpec("Budsjett", PresentationTheme.BarkNype, [new(SlideKind.Chapter, "Innledning")]));

    private static readonly GeneratedFile WordDocument = new GeneratedFile.WordDocument(
        "Notat.docx", new WordDocumentSpec("Notat", WordDocumentTemplate.Simple, [new(DocumentBlockKind.Heading1, Text: "Notat")]));

    private static readonly GeneratedFile MeetingDocument = new GeneratedFile.MeetingDocument(
        "Referat.docx", new MeetingDocumentSpec("Referat", MeetingDocumentKind.Minutes, "Styremøte", [new("1", "Budsjett")]));

    private static readonly WordTemplateFiles TemplateFiles = new();

    private static GeneratedFileBuilder Builder() =>
        new(
            new FileBuildLimiter(new FileBuildLimiterOptions(1, TimeSpan.FromSeconds(30))),
            new VestfoldPresentationBuilder(new PresentationTemplate(), PictureCatalog.Embedded, TimeProvider.System),
            new VestfoldWordDocumentBuilder(TemplateFiles, TimeProvider.System),
            new VestfoldMeetingDocumentBuilder(TemplateFiles, TimeProvider.System));

    [Fact]
    public async Task A_presentation_is_built_as_a_presentation()
    {
        using var builder = Builder();

        var built = Assert.IsType<FileBuildResult.Built>(await builder.BuildAsync(Presentation));

        using var document = PresentationDocument.Open(new MemoryStream(built.Content.ToArray()), isEditable: false);
        Assert.Equal(PresentationDocumentType.Presentation, document.DocumentType);
    }

    [Fact]
    public async Task A_word_document_is_built_as_a_word_document()
    {
        using var builder = Builder();

        var built = Assert.IsType<FileBuildResult.Built>(await builder.BuildAsync(WordDocument));

        using var document = WordprocessingDocument.Open(new MemoryStream(built.Content.ToArray()), isEditable: false);
        Assert.Equal(WordprocessingDocumentType.Document, document.DocumentType);
    }

    [Fact]
    public async Task A_meeting_document_is_built_as_a_word_document()
    {
        using var builder = Builder();

        var built = Assert.IsType<FileBuildResult.Built>(await builder.BuildAsync(MeetingDocument));

        using var document = WordprocessingDocument.Open(new MemoryStream(built.Content.ToArray()), isEditable: false);
        Assert.Equal(WordprocessingDocumentType.Document, document.DocumentType);
    }

    [Fact]
    public void Each_file_is_saved_with_its_formats_content_type()
    {
        Assert.Equal(
            (VestfoldPresentationBuilder.ContentType, VestfoldWordDocumentBuilder.ContentType, VestfoldWordDocumentBuilder.ContentType),
            (GeneratedFileBuilder.ContentTypeOf(Presentation), GeneratedFileBuilder.ContentTypeOf(WordDocument), GeneratedFileBuilder.ContentTypeOf(MeetingDocument)));
    }

    [Fact]
    public async Task A_build_after_the_circuit_has_ended_is_cancelled()
    {
        var builder = Builder();

        builder.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(Presentation));
        Assert.True(builder.IsCancelled);
    }

    [Fact]
    public void Builds_are_not_cancelled_while_the_circuit_lives()
    {
        using var builder = Builder();

        Assert.False(builder.IsCancelled);
    }
}
