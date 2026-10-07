using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Processing;

// A ready attachment in, a draft out. Knows nothing of chats, assistants or
// queues, and never deletes the temp file: whoever took it from the registry does.
public sealed class KnowledgeFileProcessor(
    DocumentConverterRegistry converters,
    IDocumentSummarizer summarizer,
    TempFileStore store,
    int maxEstimatedTokens,
    ILogger<KnowledgeFileProcessor> logger)
{
    public async Task<KnowledgeFileProcessingResult> ProcessAsync(ReadyAttachment attachment, IProgress<KnowledgeFileProcessingStage> progress, CancellationToken ct)
    {
        var contentType = attachment.Type.ContentType;
        if (!converters.TryGet(contentType, out var converter))
        {
            return new KnowledgeFileProcessingResult.Rejected(ProcessingRejections.UnsupportedType);
        }

        progress.Report(KnowledgeFileProcessingStage.Converting);
        var conversion = await ConvertAsync(converter, attachment, contentType, ct);
        if (conversion is ConversionResult.Rejected rejected)
        {
            return new KnowledgeFileProcessingResult.Rejected(rejected.Reason);
        }

        var converted = (ConversionResult.Converted)conversion;

        // After converting, before summarizing: the converter's early check
        // only rules out files certain to be too large, and a summary may cost.
        if (TokenEstimate.FromCharacters(converted.Markdown.Length) > maxEstimatedTokens)
        {
            return new KnowledgeFileProcessingResult.Rejected(ConversionRejections.TooManyTokens(maxEstimatedTokens));
        }

        var outline = DocumentOutline.Build(converted.Markdown);

        progress.Report(KnowledgeFileProcessingStage.Summarizing);
        var summary = await SummarizeAsync(attachment, converted.Markdown, outline, ct);

        return new KnowledgeFileProcessingResult.Processed(new KnowledgeFileDraft(
            attachment.FileName,
            contentType,
            attachment.SizeBytes,
            attachment.Sha256,
            converted.Markdown,
            converted.Origin,
            converted.PageCount,
            summary));
    }

    private async Task<ConversionResult> ConvertAsync(IDocumentConverter converter, ReadyAttachment attachment, string contentType, CancellationToken ct)
    {
        await using var content = store.OpenRead(attachment.File);
        return await converter.ConvertAsync(new ConversionRequest(attachment.FileName, contentType, content, maxEstimatedTokens), ct);
    }

    // A file is usable without a summary, so a summarizer that fails costs the
    // summary, not the file. Only our own cancellation ends the whole job: a
    // provider's HTTP timeout is also an OperationCanceledException, and
    // letting it through would fail the file over its summary.
    private async Task<string?> SummarizeAsync(ReadyAttachment attachment, string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct)
    {
        try
        {
            return await summarizer.SummarizeAsync(markdown, outline, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Summarizing upload {UploadId} failed; the file is kept without a summary.", attachment.UploadId);
            return null;
        }
    }
}
