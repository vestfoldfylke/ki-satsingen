using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

internal abstract record TestPdfPage;

internal sealed record TextPage(string Text) : TestPdfPage;

internal sealed record ScannedPage : TestPdfPage;

internal sealed record BlankPage : TestPdfPage;

// One line of text placed on the page. Y counts up from the bottom, as in PDF.
internal sealed record PlacedLine(string Text, double Y, double FontSize = TestPdf.BodyFontSize, bool IsBold = false, double X = TestPdf.LeftMargin);

// PDFs built in memory, so each test shows the document it is about.
internal static class TestPdf
{
    public const double BodyFontSize = 12;
    public const double LeftMargin = 72;
    private const double FirstLineY = 720;

    // A valid 1 x 1 PNG, standing in for a scanned page.
    private static readonly byte[] ScanImage = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // An embedded font, as in real PDFs. PdfPig's built-in fonts cannot draw
    // "å", so they would keep Norwegian text out of the tests.
    private static readonly byte[] NotoSans =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSans-Regular.ttf"));

    public static byte[] Pages(params TestPdfPage[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(NotoSans);

        foreach (var page in pages)
        {
            var pageBuilder = builder.AddPage(PageSize.A4);
            switch (page)
            {
                case TextPage text:
                    pageBuilder.AddText(text.Text, BodyFontSize, new PdfPoint(LeftMargin, FirstLineY), font);
                    break;
                case ScannedPage:
                    pageBuilder.AddPng(ScanImage, new PdfRectangle(0, 0, pageBuilder.PageSize.Width, pageBuilder.PageSize.Height));
                    break;
            }
        }

        return builder.Build();
    }

    // A single page with each line where the test puts it.
    public static byte[] Page(params PlacedLine[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var pageBuilder = builder.AddPage(PageSize.A4);

        foreach (var line in lines)
        {
            pageBuilder.AddText(line.Text, line.FontSize, new PdfPoint(line.X, line.Y), line.IsBold ? bold : regular);
        }

        return builder.Build();
    }

    // PdfPig cannot write encrypted PDFs, so this adds a standard security
    // handler whose user key matches no password, the empty one included. To a
    // reader without the password that is what a protected file looks like; it
    // refuses before reading any object, so the objects need not be encrypted.
    public static byte[] PasswordProtected()
    {
        var pdf = Encoding.Latin1.GetString(Pages(new TextPage("Hemmelig")));
        var unmatchedKey = new string('0', 64);
        var encrypt = $"/Encrypt << /Filter /Standard /V 1 /R 2 /Length 40 /P -4 /O <{unmatchedKey}> /U <{unmatchedKey}> >> ";

        const string trailerStart = "trailer\n<<";
        if (!pdf.Contains(trailerStart, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"PdfPig no longer writes '{trailerStart}', so the encryption dictionary has nowhere to go. Update {nameof(PasswordProtected)} to its new trailer layout.");
        }

        return Encoding.Latin1.GetBytes(pdf.Replace(trailerStart, trailerStart + encrypt, StringComparison.Ordinal));
    }
}
