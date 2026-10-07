using System.Globalization;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Stand-in until images are read by a vision model: proves the pipeline takes
// binary input, and already enforces the size limit that model will need. The
// replacement keeps the limit and changes only what Markdown comes out.
public sealed class ImagePlaceholderConverter(long maxSidePixels, long maxTotalPixels) : IDocumentConverter
{
    public IReadOnlyList<string> ContentTypes { get; } =
        [AttachmentContentTypes.Png.ContentType, AttachmentContentTypes.Jpeg.ContentType];

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        if (await ImageHeaderReader.ReadAsync(request.Content, request.ContentType, ct) is not { } size || size.Width == 0 || size.Height == 0)
        {
            return new ConversionResult.Rejected(ConversionRejections.MalformedImage);
        }

        // Checked on the header's claim, before anything is decoded: a valid
        // header can promise a size that would take gigabytes to decode.
        if (size.Width > maxSidePixels || size.Height > maxSidePixels || size.Width * size.Height > maxTotalPixels)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageTooLarge(size.Width, size.Height));
        }

        var markdown = string.Create(CultureInfo.InvariantCulture, $"Bilde: {size.Width} x {size.Height} px. Innholdet er ikke tolket ennå.");
        return new ConversionResult.Converted(markdown, ContentOrigin.ImagePlaceholder, PageCount: null);
    }
}
