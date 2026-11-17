using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;
using Microsoft.Extensions.AI;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Reads an image with a vision model: a description, then its text as written.
// The model has no tools, so instructions written in an image can at most
// corrupt the transcription, which chat models then read as data.
internal sealed class ImageDocumentConverter(ImageConversionModel model, ILogger<ImageDocumentConverter> logger) : IDocumentConverter
{
    // Far above any screenshot. Checked on the header's claim alone, since
    // nothing here decodes the image.
    public const long MaxSidePixels = 8_000;

    // A header claiming more than 40 MP is a pixel bomb, not a screenshot.
    public const long MaxTotalPixels = 40_000_000;

    // The default bufferSize documented for Stream.CopyToAsync; any positive
    // size reads correctly.
    private const int ReadChunkBytes = 81_920;

    // The description comes first because the summary reads the file's opening.
    private const string Instructions =
        """
        Beskriv bildet og skriv av all tekst i det. Svar bare med Markdown i denne formen, uten kodeblokk rundt:

        ## Beskrivelse av bildet
        Hva slags bilde det er, hva det handler om, og hvordan innholdet er satt opp. Ta med det som ikke står som tekst, men trengs for å forstå bildet, som avkryssinger, markeringer, statusikoner, feilmeldinger og hva et diagram viser. Noen få setninger.

        ## Tekst i bildet
        All tekst i bildet, ordrett, på originalspråket og i leserekkefølge.
        - Ikke forkort eller oppsummer. Tall, datoer, beløp, navn og saksnumre skrives nøyaktig som de står.
        - Behold strukturen: overskrifter i bildet som ###, lister som lister og tabeller som Markdown-tabeller.
        - Skriv [uleselig] der teksten ikke kan leses sikkert, i stedet for å gjette.
        """;

    public IReadOnlyList<string> ContentTypes { get; } =
        [AttachmentContentTypes.Png.ContentType, AttachmentContentTypes.Jpeg.ContentType];

    public bool IsRemote => true;

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        if (await ReadWithinLimitAsync(request.Content, model.MaxImageBytes, ct) is not { } image)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageFileTooLarge(model.MaxImageBytes));
        }

        if (await ImageHeaderReader.ReadAsync(new MemoryStream(image), request.ContentType, ct) is not { } size || size.Width == 0 || size.Height == 0)
        {
            return new ConversionResult.Rejected(ConversionRejections.MalformedImage);
        }

        if (size.Width > MaxSidePixels || size.Height > MaxSidePixels || size.Width * size.Height > MaxTotalPixels)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageTooLarge(size.Width, size.Height));
        }

        if (await DescribeAsync(request.FileName, image, request.ContentType, ct) is not { } response)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageNotRead);
        }

        var tokenUsage = ToTokenUsage(response.Usage);
        if (response.FinishReason == ChatFinishReason.Length)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageTextTooLong, tokenUsage);
        }

        // Any end other than a finished answer may have cut the text short, and
        // the same image would end the same way. A provider that sends no reason
        // is taken as finished.
        var isFinished = response.FinishReason is null || response.FinishReason == ChatFinishReason.Stop;
        if (!isFinished)
        {
            return new ConversionResult.Rejected(ConversionRejections.ImageNotReadable, tokenUsage);
        }

        var markdown = response.Text.Trim();
        return markdown.Length > 0
            ? new ConversionResult.Converted(markdown, ContentOrigin.ImageDescription, PageCount: null, tokenUsage)
            : new ConversionResult.Rejected(ConversionRejections.ImageNotRead, tokenUsage);
    }

    // Null as soon as the bytes read exceed maxBytes, so a file over the limit
    // is never held whole.
    private static async Task<byte[]?> ReadWithinLimitAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using var image = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (image.Length + read > maxBytes)
            {
                return null;
            }

            image.Write(chunk, 0, read);
        }

        return image.ToArray();
    }

    // Only the provider call is caught, so a bug in the converter still fails
    // loudly. Our own cancellation is rethrown so the pipeline can tell a stop
    // from its timeout; the SDK's own timeout counts as a provider failure.
    private async Task<ChatResponse?> DescribeAsync(string fileName, byte[] image, string contentType, CancellationToken ct)
    {
        try
        {
            return await model.Client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, [new TextContent(Instructions), new DataContent(image, contentType)])],
                model.Options,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reading image {FileName} with {ModelId} failed, so the image is refused.", fileName, model.ModelId);
            return null;
        }
    }

    private ConversionTokenUsage? ToTokenUsage(UsageDetails? details) =>
        MessageUsage.FromCounts(
            inputTokens: details?.InputTokenCount,
            outputTokens: details?.OutputTokenCount,
            estimatedInputTokens: null,
            estimatedOutputTokens: null,
            cachedInputTokens: details?.CachedInputTokenCount,
            reasoningTokens: details?.ReasoningTokenCount) is { } usage
            ? new ConversionTokenUsage(model.Provider, model.ModelId, usage)
            : null;
}
