using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

internal static class DocxConverter
{
    // Throws OperationCanceledException as soon as ct is cancelled, also
    // before any work starts.
    public static OfficeConversionResult Convert(Stream stream, OfficeLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        DocxContent content;

        try
        {
            if (PackageCheck.ExceedsDeclaredSize(stream, limits.MaxUncompressedBytes))
            {
                return OfficeConversionResult.TooLarge(ConversionRejections.DocxTooLargeUncompressed);
            }

            content = Load(stream, ct);
        }
        catch (Exception ex) when (IsUnreadableFile(ex))
        {
            return OfficeConversionResult.Unreadable(ConversionRejections.ErrorParsingDocx, ex);
        }

        var output = new MarkdownOutput(limits.MaxCharacters);

        try
        {
            var writtenTextCharacters = DocxBodyWriter.Write(content, output, ct);
            var sourceTextCharacters = DocxTextCount.Of(content, ct);

            // A table ends its last line itself.
            return OfficeConversionResult.Converted(output.ToString().TrimEnd('\n'), writtenTextCharacters, sourceTextCharacters);
        }
        catch (MarkdownTooLongException ex)
        {
            return OfficeConversionResult.TooLong(ConversionRejections.TooManyTokens(limits.MaxEstimatedTokens), ex);
        }
    }

    // The only code that touches the file. A missing main part or body is a
    // broken file, not a null for the writer to check.
    // Every part is loaded through CheckedParts, which checks ct as it reads.
    private static DocxContent Load(Stream stream, CancellationToken ct)
    {
        stream.Position = 0;

        using var document = WordprocessingDocument.Open(stream, isEditable: false, CheckedParts.OpenSettings);

        var mainPart = document.MainDocumentPart ?? throw new InvalidDataException("The package has no main document part.");
        var body = CheckedParts.RootOf<Document>(mainPart, ct)?.Body ?? throw new InvalidDataException("The main document has no body.");

        return new DocxContent(
            body,
            DocxStyles.FromXml(CheckedParts.RootOf<Styles>(mainPart.StyleDefinitionsPart, ct)),
            DocxLists.FromXml(CheckedParts.RootOf<Numbering>(mainPart.NumberingDefinitionsPart, ct)),
            ReadHyperlinks(mainPart),
            ReadNotes(CheckedParts.RootOf<Footnotes>(mainPart.FootnotesPart, ct)?.Elements<Footnote>(), mainPart.FootnotesPart),
            ReadNotes(CheckedParts.RootOf<Endnotes>(mainPart.EndnotesPart, ct)?.Elements<Endnote>(), mainPart.EndnotesPart));
    }

    private static DocxNotes ReadNotes(IEnumerable<FootnoteEndnoteType>? notes, OpenXmlPart? part)
    {
        if (notes is null || part is null)
        {
            return DocxNotes.None;
        }

        var notesById = new Dictionary<long, FootnoteEndnoteType>();
        foreach (var note in notes)
        {
            if (OpenXmlValues.ValueOf(note.Id) is { } id)
            {
                notesById.TryAdd(id, note);
            }
        }

        return new DocxNotes(notesById, ReadHyperlinks(part));
    }

    private static DocxHyperlinks ReadHyperlinks(OpenXmlPart part)
    {
        var urlsById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in part.HyperlinkRelationships)
        {
            var uri = relationship.Uri;
            urlsById.TryAdd(relationship.Id, uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString);
        }

        return new DocxHyperlinks(urlsById);
    }

    // What the SDK and our checks throw on a broken file. Anything else is a
    // bug in this code and must reach the caller as one.
    private static bool IsUnreadableFile(Exception exception) => exception is
        FileFormatException or InvalidDataException or XmlException or
        InvalidMCContentException or OpenXmlPackageException or
        UriFormatException or InvalidOperationException;
}
