using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// Hand-built XML tests pass while documents from real tools still fail, so
// every document in Documents/ is converted and compared with the Markdown
// beside it. Only documents with nothing sensitive in them belong there.
//
// To add one: put the .docx in Documents/, review its conversion line by
// line, and save that as a .md with the same name.
public sealed class DocxRealDocumentTests
{
    private static readonly string DocumentsFolder = Path.Combine(AppContext.BaseDirectory, "Services", "KnowledgeFiles", "Conversion", "Office", "Docx", "Documents");

    public static TheoryData<string> DocumentNames()
    {
        var names = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(DocumentsFolder, "*.docx"))
        {
            names.Add(Path.GetFileNameWithoutExtension(path));
        }

        return names;
    }

    // The .md file's last newline is an editor's, not part of the Markdown.
    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void A_real_document_converts_to_its_reviewed_markdown(string name)
    {
        var docx = File.ReadAllBytes(Path.Combine(DocumentsFolder, $"{name}.docx"));
        var expected = File.ReadAllText(Path.Combine(DocumentsFolder, $"{name}.md")).ReplaceLineEndings("\n").TrimEnd('\n');

        var markdown = ToMarkdown(docx);

        Assert.Equal(expected, markdown);
    }

    // The test above passes vacuously if the documents aren't copied to the output.
    [Fact]
    public void There_is_at_least_one_real_document_to_compare()
    {
        var names = DocumentNames();

        Assert.NotEmpty(names);
    }
}
