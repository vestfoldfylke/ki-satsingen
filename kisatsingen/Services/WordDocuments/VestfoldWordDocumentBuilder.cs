using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Turns a validated spec into a .docx built on the Vestfold template it names.
// The same spec gives the same document; only the file's created time differs.
public sealed class VestfoldWordDocumentBuilder(WordTemplateFiles templateFiles, TimeProvider timeProvider)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string FileExtension = ".docx";
    public const string FallbackFileStem = "dokument";

    // A4 with the simple template's margins, used only if a template's section
    // leaves its page size or margins out.
    private const int FallbackContentWidth = 9524;

    public ArraySegment<byte> Build(WordDocumentSpec spec)
    {
        var templateFile = spec.Template switch
        {
            WordDocumentTemplate.Simple => WordTemplateFile.Simple,
            WordDocumentTemplate.Report => WordTemplateFile.Report,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Template, "A Word template with no template file; add it to VestfoldWordDocumentBuilder.")
        };

        return WordTemplateBuild.Run(
            templateFiles.OpenWritableCopy(templateFile),
            spec.Title,
            timeProvider.GetUtcNow().UtcDateTime,
            mainPart => Fill(mainPart, spec));
    }

    private static void Fill(MainDocumentPart mainPart, WordDocumentSpec spec)
    {
        var body = mainPart.Document?.Body
            ?? throw new InvalidOperationException($"The {spec.Template} Word template has no document body; the template file is damaged.");

        WordStyles.EnsurePresent(mainPart, spec.Template);

        var layout = spec.Template == WordDocumentTemplate.Report
            ? ReportLayout.Prepare(body, spec)
            : SimpleLayout.Prepare(body);

        ContentControls.FillTitleFields(mainPart, spec.Title);
        AddBlocks(layout, mainPart, spec);
    }

    private static void AddBlocks(WordBodyLayout layout, MainDocumentPart mainPart, WordDocumentSpec spec)
    {
        var numbering = WordNumbering.Prepare(mainPart);
        var contentWidth = ContentWidth(layout.ContentSection);
        var tableFormat = WordStyles.TableFormatFor(spec.Template);
        var insertBefore = layout.InsertBefore;

        foreach (var block in spec.Blocks)
        {
            // Two tables in a row would merge into one when Word opens the file.
            if (block.Kind == DocumentBlockKind.Table && insertBefore.PreviousSibling() is Table)
            {
                insertBefore.InsertBeforeSelf(new Paragraph());
            }

            foreach (var element in WordBlockFactory.Create(block, numbering, contentWidth, tableFormat))
            {
                insertBefore.InsertBeforeSelf(element);
            }
        }

        // Word needs a paragraph between a table and what follows it, or the
        // file opens as damaged.
        if (insertBefore.PreviousSibling() is Table)
        {
            insertBefore.InsertBeforeSelf(new Paragraph());
        }
    }

    private static int ContentWidth(SectionProperties section)
    {
        var pageWidth = section.GetFirstChild<PageSize>()?.Width?.Value;
        var margins = section.GetFirstChild<PageMargin>();
        if (pageWidth is null || margins?.Left?.Value is not { } left || margins.Right?.Value is not { } right)
        {
            return FallbackContentWidth;
        }

        return (int)pageWidth.Value - (int)left - (int)right;
    }
}
