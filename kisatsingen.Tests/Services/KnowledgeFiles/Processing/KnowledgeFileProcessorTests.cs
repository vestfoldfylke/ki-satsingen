using System.Text;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;
using kisatsingen.Services.KnowledgeFiles.Processing;
using kisatsingen.Tests.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Processing;

public sealed class KnowledgeFileProcessorTests : IDisposable
{
    private const int GenerousTokenCap = 1_000_000;

    private readonly AttachmentTestEnvironment _environment = new();

    public void Dispose() => _environment.Dispose();

    private KnowledgeFileProcessor Processor(int maxEstimatedTokens = GenerousTokenCap, IDocumentSummarizer? summarizer = null) =>
        new(
            new DocumentConverterRegistry([new TextDocumentConverter()]),
            summarizer ?? new OpeningExcerptSummarizer(),
            _environment.Store,
            maxEstimatedTokens,
            NullLogger<KnowledgeFileProcessor>.Instance);

    private ReadyAttachment Markdown(string text) =>
        ReadyAttachments.From(_environment, Encoding.UTF8.GetBytes(text), AttachmentContentTypes.Markdown, "notat.md");

    private static Task<KnowledgeFileProcessingResult> ProcessAsync(KnowledgeFileProcessor processor, ReadyAttachment attachment, IProgress<KnowledgeFileProcessingStage>? progress = null) =>
        processor.ProcessAsync(attachment, progress ?? new RecordingProgress(), CancellationToken.None);

    [Fact]
    public async Task A_markdown_attachment_becomes_a_draft_of_its_text_and_where_it_came_from()
    {
        var attachment = Markdown("# Budsjett\n\nTall for 2027.");

        var result = await ProcessAsync(Processor(), attachment);

        var draft = Assert.IsType<KnowledgeFileProcessingResult.Processed>(result).Draft;
        Assert.Equal(
            ("notat.md", "# Budsjett\n\nTall for 2027.", ContentOrigin.TextFile, attachment.Sha256),
            (draft.FileName, draft.Markdown, draft.Origin, draft.Sha256));
    }

    [Fact]
    public async Task A_converter_refusal_reaches_the_caller_with_its_reason()
    {
        var binary = ReadyAttachments.From(_environment, [0x01, 0x00, 0x02], AttachmentContentTypes.PlainText);

        var result = await ProcessAsync(Processor(), binary);

        Assert.Equal(new KnowledgeFileProcessingResult.Rejected(ConversionRejections.Binary), result);
    }

    // The converter's early check only rules out files certain to be too large.
    [Fact]
    public async Task A_file_over_the_token_cap_after_converting_is_refused_before_it_is_summarized()
    {
        var summarizer = new CountingSummarizer();

        var result = await ProcessAsync(Processor(maxEstimatedTokens: 3, summarizer), Markdown("# Tittel\n\nMer enn ni tegn."));

        Assert.IsType<KnowledgeFileProcessingResult.Rejected>(result);
        Assert.Equal(0, summarizer.Calls);
    }

    [Fact]
    public async Task A_summarizer_that_fails_costs_the_summary_not_the_file()
    {
        var result = await ProcessAsync(Processor(summarizer: new FailingSummarizer()), Markdown("# Tittel\n\nTekst."));

        Assert.Null(Assert.IsType<KnowledgeFileProcessingResult.Processed>(result).Draft.Summary);
    }

    // A provider's HTTP timeout is a TaskCanceledException nobody here asked for.
    [Fact]
    public async Task A_summarizer_that_times_out_costs_the_summary_not_the_file()
    {
        var result = await ProcessAsync(Processor(summarizer: new TimingOutSummarizer()), Markdown("# Tittel\n\nTekst."));

        Assert.Null(Assert.IsType<KnowledgeFileProcessingResult.Processed>(result).Draft.Summary);
    }

    [Fact]
    public async Task A_content_type_with_no_converter_is_refused()
    {
        var unknown = ReadyAttachments.From(_environment, [1, 2, 3], new AttachmentType("application/x-unknown", Signature: null));

        var result = await ProcessAsync(Processor(), unknown);

        Assert.IsType<KnowledgeFileProcessingResult.Rejected>(result);
    }

    [Fact]
    public async Task Progress_is_reported_in_pipeline_order()
    {
        var progress = new RecordingProgress();

        await ProcessAsync(Processor(), Markdown("tekst"), progress);

        Assert.Equal([KnowledgeFileProcessingStage.Converting, KnowledgeFileProcessingStage.Summarizing], progress.Reported);
    }

    // Deleting it is the job of whoever took it, so a caller can still retry with it.
    [Fact]
    public async Task Processing_leaves_the_temp_file_to_whoever_took_it()
    {
        var attachment = Markdown("tekst");

        await ProcessAsync(Processor(), attachment);

        Assert.True(File.Exists(attachment.File.Path));
    }

    private sealed class CountingSummarizer : IDocumentSummarizer
    {
        public int Calls { get; private set; }

        public Task<string?> SummarizeAsync(string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<string?>("sammendrag");
        }
    }

    private sealed class FailingSummarizer : IDocumentSummarizer
    {
        public Task<string?> SummarizeAsync(string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct) =>
            throw new HttpRequestException("model unavailable");
    }

    private sealed class TimingOutSummarizer : IDocumentSummarizer
    {
        public Task<string?> SummarizeAsync(string markdown, IReadOnlyList<OutlineEntry> outline, CancellationToken ct) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }
}
