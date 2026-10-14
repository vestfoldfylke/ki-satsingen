using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;

namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

// A file in, a knowledge-file draft out, through fixed steps in order:
// convert, check size, outline, summarize. Knows nothing of chats or
// assistants, and never deletes the temp file: whoever owns it does.
public sealed class KnowledgeFilePipeline(
    DocumentConverterRegistry converters,
    IDocumentSummarizer summarizer,
    AttachmentStorage storage,
    KnowledgeFilePipelineOptions options,
    KnowledgeFilePipelineMetrics metrics,
    int maxEstimatedTokens,
    ILogger<KnowledgeFilePipeline> logger)
{
    // The metric's result label for one conversion.
    private enum ConversionResultLabel
    {
        Converted,
        Rejected,
        TimedOut,
        Failed
    }

    // Shared by every user's conversions.
    private readonly SemaphoreSlim _conversionSlots = new(options.MaxConcurrentConversions);

    internal async Task<KnowledgeFilePipelineResult> RunAsync(ReadyAttachment attachment, CancellationToken ct)
    {
        if (!converters.TryGet(attachment.Type.ContentType, out var converter))
        {
            return new KnowledgeFilePipelineResult.Rejected(PipelineRejections.UnsupportedType);
        }

        var conversion = await ConvertAsync(converter, attachment, ct);
        if (conversion is ConversionResult.Rejected rejected)
        {
            return new KnowledgeFilePipelineResult.Rejected(rejected.Reason);
        }

        var converted = (ConversionResult.Converted)conversion;

        // After converting, before summarizing: the converter's early check
        // only rules out files certain to be too large, and a summary may cost.
        if (TokenEstimate.FromCharacters(converted.Markdown.Length) > maxEstimatedTokens)
        {
            return new KnowledgeFilePipelineResult.Rejected(ConversionRejections.TooManyTokens(maxEstimatedTokens));
        }

        var outline = DocumentOutline.Build(converted.Markdown);
        var summary = await SummarizeAsync(attachment, converted.Markdown, outline, ct);

        return new KnowledgeFilePipelineResult.Completed(new KnowledgeFileDraft(
            attachment.FileName,
            attachment.Type.ContentType,
            attachment.SizeBytes,
            attachment.Sha256,
            converted.Markdown,
            converted.Origin,
            converted.PageCount,
            summary));
    }

    // The timeout starts once a slot is free, so waiting for one never counts
    // against it. The conversion runs off the circuit: on it, a large file
    // would freeze that user's page.
    private async Task<ConversionResult> ConvertAsync(IDocumentConverter converter, ReadyAttachment attachment, CancellationToken ct)
    {
        await _conversionSlots.WaitAsync(ct);

        ConversionResultLabel? resultLabel = ConversionResultLabel.Failed;
        var duration = metrics.StartConversion();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ConversionTimeout);

        try
        {
            var conversion = await Task.Run(async () =>
            {
                await using var content = storage.OpenRead(attachment.File);
                var request = new ConversionRequest(attachment.FileName, attachment.Type.ContentType, content, maxEstimatedTokens);
                return await converter.ConvertAsync(request, timeout.Token);
            }, CancellationToken.None);

            resultLabel = conversion is ConversionResult.Converted ? ConversionResultLabel.Converted : ConversionResultLabel.Rejected;
            return conversion;
        }
        // The order matters: the turn's own stop is a cancellation, and only
        // the timer left is the timeout. A stop is not counted: nothing went
        // wrong, and its duration says nothing about converting.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            resultLabel = null;
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            resultLabel = ConversionResultLabel.TimedOut;
            logger.LogWarning("Converting attachment {AttachmentId} ({ContentType}) timed out after {Timeout}.", attachment.AttachmentId, attachment.Type.ContentType, options.ConversionTimeout);
            return new ConversionResult.Rejected(PipelineRejections.TimedOut);
        }
        finally
        {
            _conversionSlots.Release();
            if (resultLabel is ConversionResultLabel label)
            {
                metrics.Observe(duration);
                metrics.CountOutcome(attachment.Type.ContentType, label.ToString());
            }
        }
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
            logger.LogWarning(ex, "Summarizing attachment {AttachmentId} failed; the file is kept without a summary.", attachment.AttachmentId);
            return null;
        }
    }
}
