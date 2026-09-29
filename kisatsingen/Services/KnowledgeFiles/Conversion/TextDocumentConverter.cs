using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Plain text and Markdown share this: plain text is valid Markdown for our
// purposes, so both are the decoded text, used as it is.
public sealed class TextDocumentConverter : IDocumentConverter
{
    // UTF-8 spends at most four bytes on a character, so a file's byte count
    // divided by this is the fewest characters it can hold.
    private const int MaxBytesPerCharacter = 4;

    public IReadOnlyList<string> ContentTypes { get; } =
        [AttachmentContentTypes.PlainText.ContentType, AttachmentContentTypes.Markdown.ContentType];

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        // Refused before reading: even decoded as compactly as possible, a file
        // this large would be over the limit.
        if (request.Content.CanSeek && FewestTokens(request.Content.Length) > request.MaxEstimatedTokens)
        {
            return new ConversionResult.Rejected(ConversionRejections.TooManyTokens(request.MaxEstimatedTokens));
        }

        var bytes = await ReadAllAsync(request.Content, ct);

        return TextDecoder.Decode(bytes) switch
        {
            TextDecodeResult.Decoded decoded => new ConversionResult.Converted(decoded.Text, ContentOrigin.TextFile, PageCount: null),
            TextDecodeResult.Rejected rejected => new ConversionResult.Rejected(rejected.Reason),
            var other => throw new InvalidOperationException($"TextDocumentConverter has no rule for {other.GetType().Name}.")
        };
    }

    private static long FewestTokens(long byteCount) =>
        TokenEstimate.FromCharacters(byteCount / MaxBytesPerCharacter);

    // Whole, because decoding needs all of it. Bounded by the early check: a
    // file that passes it is a few MB at most for any sensible token cap.
    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
