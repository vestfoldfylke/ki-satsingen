using System.Text;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Documents;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Vestfold.Extensions.Metrics.Services;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Pipeline;

public sealed class KnowledgeFilePipelineTests : IDisposable
{
    private const int GenerousTokenCap = 1_000_000;
    private static readonly KnowledgeFilePipelineOptions OneSlot = new() { MaxConcurrentConversions = 1 };

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly StallingConverter _stalling = new();
    private readonly SlotHoldingConverter _slotHolding = new();

    public void Dispose() => _environment.Dispose();

    private KnowledgeFilePipeline Pipeline(
        int maxEstimatedTokens = GenerousTokenCap,
        IDocumentSummarizer? summarizer = null,
        KnowledgeFilePipelineOptions? options = null,
        IMetricsService? metrics = null) =>
        new(
            new DocumentConverterRegistry([new TextDocumentConverter(), _stalling, _slotHolding, new ThrowingConverter()]),
            summarizer ?? new OpeningExcerptSummarizer(),
            _environment.Storage,
            options ?? new KnowledgeFilePipelineOptions(),
            new KnowledgeFilePipelineMetrics(metrics ?? new RecordingMetricsService(), NullLogger<KnowledgeFilePipelineMetrics>.Instance),
            maxEstimatedTokens,
            NullLogger<KnowledgeFilePipeline>.Instance);

    private ReadyAttachment Markdown(string text) =>
        ReadyAttachments.From(_environment, Encoding.UTF8.GetBytes(text), AttachmentContentTypes.Markdown, "notat.md");

    private ReadyAttachment OfType(AttachmentType type) => ReadyAttachments.From(_environment, [1, 2, 3], type);

    private static Task<KnowledgeFilePipelineResult> RunAsync(KnowledgeFilePipeline pipeline, ReadyAttachment attachment, CancellationToken ct = default) =>
        pipeline.RunAsync(attachment, ct);

    [Fact]
    public async Task A_markdown_attachment_becomes_a_new_knowledge_file_of_its_text_and_where_it_came_from()
    {
        var attachment = Markdown("# Budsjett\n\nTall for 2027.");

        var result = await RunAsync(Pipeline(), attachment);

        var newFile = Assert.IsType<KnowledgeFilePipelineResult.Completed>(result).File;
        Assert.Equal(
            ("notat.md", "# Budsjett\n\nTall for 2027.", ContentOrigin.TextFile, attachment.Sha256),
            (newFile.FileName, newFile.Markdown, newFile.Origin, newFile.Sha256));
    }

    [Fact]
    public async Task A_converter_refusal_reaches_the_caller_with_its_reason()
    {
        var binary = ReadyAttachments.From(_environment, [0x01, 0x00, 0x02], AttachmentContentTypes.PlainText);

        var result = await RunAsync(Pipeline(), binary);

        Assert.Equal(new KnowledgeFilePipelineResult.Rejected(ConversionRejections.Binary), result);
    }

    // The converter's early check only rules out files certain to be too large.
    [Fact]
    public async Task A_file_over_the_token_cap_after_converting_is_refused_before_it_is_summarized()
    {
        var summarizer = new CountingSummarizer();

        var result = await RunAsync(Pipeline(maxEstimatedTokens: 3, summarizer), Markdown("# Tittel\n\nMer enn ni tegn."));

        Assert.IsType<KnowledgeFilePipelineResult.Rejected>(result);
        Assert.Equal(0, summarizer.Calls);
    }

    [Fact]
    public async Task A_summarizer_that_fails_costs_the_summary_not_the_file()
    {
        var result = await RunAsync(Pipeline(summarizer: new FailingSummarizer()), Markdown("# Tittel\n\nTekst."));

        Assert.Null(Assert.IsType<KnowledgeFilePipelineResult.Completed>(result).File.Summary);
    }

    // A provider's HTTP timeout is a TaskCanceledException nobody here asked for.
    [Fact]
    public async Task A_summarizer_that_times_out_costs_the_summary_not_the_file()
    {
        var result = await RunAsync(Pipeline(summarizer: new TimingOutSummarizer()), Markdown("# Tittel\n\nTekst."));

        Assert.Null(Assert.IsType<KnowledgeFilePipelineResult.Completed>(result).File.Summary);
    }

    [Fact]
    public async Task A_content_type_with_no_converter_is_refused()
    {
        var unknown = ReadyAttachments.From(_environment, [1, 2, 3], new AttachmentType("application/x-unknown", Signature: null));

        var result = await RunAsync(Pipeline(), unknown);

        Assert.IsType<KnowledgeFilePipelineResult.Rejected>(result);
    }

    [Fact]
    public async Task A_conversion_that_runs_past_the_timeout_is_refused_with_a_reason()
    {
        var pipeline = Pipeline(options: new KnowledgeFilePipelineOptions { ConversionTimeout = TimeSpan.FromMilliseconds(50) });

        var result = await RunAsync(pipeline, OfType(StallingConverter.Type));

        Assert.Equal(new KnowledgeFilePipelineResult.Rejected(PipelineRejections.TimedOut), result);
    }

    [Fact]
    public async Task A_converter_bug_reaches_the_caller_as_an_exception()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Pipeline(), OfType(ThrowingConverter.Type)));
    }

    [Fact]
    public async Task A_stop_reaches_the_caller_as_a_cancellation_rather_than_a_timeout()
    {
        using var stop = new CancellationTokenSource();
        var conversion = RunAsync(Pipeline(), OfType(StallingConverter.Type), stop.Token);
        await _stalling.WaitUntilStartedAsync();

        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversion);
    }

    [Fact]
    public async Task With_every_slot_busy_a_conversion_waits_until_one_is_free()
    {
        var pipeline = Pipeline(options: OneSlot);
        var first = RunAsync(pipeline, OfType(SlotHoldingConverter.Type));
        await _slotHolding.WaitUntilStartedAsync();
        var second = RunAsync(pipeline, OfType(StallingConverter.Type));
        await Task.Delay(100);
        var startedWhileBusy = _stalling.StartedCount;

        _slotHolding.Release();
        _stalling.Release();
        await Task.WhenAll(first, second);

        Assert.Equal(0, startedWhileBusy);
    }

    // Otherwise files would time out under load because of other users'
    // conversions.
    [Fact]
    public async Task The_timeout_starts_only_once_a_slot_is_free()
    {
        var pipeline = Pipeline(options: new KnowledgeFilePipelineOptions { MaxConcurrentConversions = 1, ConversionTimeout = TimeSpan.FromMilliseconds(100) });
        var holdingTheSlot = RunAsync(pipeline, OfType(SlotHoldingConverter.Type));
        await _slotHolding.WaitUntilStartedAsync();
        var waiting = RunAsync(pipeline, Markdown("# Tittel\n\nTekst."));

        await Task.Delay(300);
        _slotHolding.Release();
        await holdingTheSlot;

        Assert.IsType<KnowledgeFilePipelineResult.Completed>(await waiting);
    }

    [Fact]
    public async Task A_slot_is_freed_after_a_conversion_that_throws()
    {
        var pipeline = Pipeline(options: OneSlot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(pipeline, OfType(ThrowingConverter.Type)));

        var next = await RunAsync(pipeline, Markdown("# Tittel\n\nTekst.")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<KnowledgeFilePipelineResult.Completed>(next);
    }

    [Fact]
    public async Task A_metrics_failure_does_not_fail_the_file()
    {
        var result = await RunAsync(Pipeline(metrics: new BrokenMetricsService()), Markdown("# Tittel\n\nTekst."));

        Assert.IsType<KnowledgeFilePipelineResult.Completed>(result);
    }

    // Deleting it is the job of whoever took it, so a caller can still retry with it.
    [Fact]
    public async Task The_pipeline_leaves_the_temp_file_to_whoever_took_it()
    {
        var attachment = Markdown("tekst");

        await RunAsync(Pipeline(), attachment);

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
