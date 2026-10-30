using kisatsingen.Data.Repositories;
using kisatsingen.Services.Assistants;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using kisatsingen.Tests.Services.KnowledgeFiles.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Assistants;

// The real pipeline. Only text converts and ThrowingConverter always fails,
// so a PNG is a file the pipeline refuses.
public sealed class AssistantFileProcessorTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssistantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly FakeAssistantFileRepository _repository = new();
    private readonly AssistantFileProcessor _processor;

    public AssistantFileProcessorTests() =>
        _processor = new AssistantFileProcessor(CreatePipeline(), _repository, NullLogger.Instance);

    public void Dispose() => _environment.Dispose();

    private KnowledgeFilePipeline CreatePipeline() =>
        new(
            new DocumentConverterRegistry([new TextDocumentConverter(), new ThrowingConverter()]),
            new OpeningExcerptSummarizer(),
            _environment.Storage,
            new KnowledgeFilePipelineOptions(),
            new KnowledgeFilePipelineMetrics(new RecordingMetricsService(), NullLogger<KnowledgeFilePipelineMetrics>.Instance),
            maxEstimatedTokens: 1_000_000,
            NullLogger<KnowledgeFilePipeline>.Instance);

    private Task<AssistantFileResult> ProcessAsync(byte[] content, AttachmentType type, CancellationToken ct = default) =>
        _processor.ProcessAsync(UserId, AssistantId, ReadyAttachments.From(_environment, content, type, "notat.md"), ct);

    [Fact]
    public async Task A_file_is_saved_to_the_assistant_under_its_name_and_its_temp_file_deleted()
    {
        var result = await ProcessAsync(UploadBytes.Text, AttachmentContentTypes.Markdown);

        Assert.Equal("notat.md", Assert.IsType<AssistantFileResult.Saved>(result).File.FileName);
        Assert.Equal(AssistantId, Assert.Single(_repository.Saved).AssistantId);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_the_pipeline_refuses_is_rejected_with_its_reason_and_never_saved()
    {
        var result = await ProcessAsync(UploadBytes.Png, AttachmentContentTypes.Png);

        Assert.Equal(new AssistantFileResult.Rejected(PipelineRejections.UnsupportedType), result);
        Assert.Empty(_repository.Saved);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_whose_converter_fails_is_rejected_as_not_processed_and_leaves_nothing_on_disk()
    {
        var result = await ProcessAsync(UploadBytes.Text, ThrowingConverter.Type);

        Assert.Equal(new AssistantFileResult.Rejected("Filen kunne ikke behandles. Prøv igjen."), result);
        Assert.Empty(_repository.Saved);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_the_repository_refuses_is_rejected_with_its_reason()
    {
        _repository.RejectionReason = KnowledgeFileSaveRejections.AssistantFull;

        var result = await ProcessAsync(UploadBytes.Text, AttachmentContentTypes.Markdown);

        Assert.Equal(new AssistantFileResult.Rejected(KnowledgeFileSaveRejections.AssistantFull), result);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_save_that_fails_is_rejected_as_not_saved()
    {
        _repository.FailSave = true;

        var result = await ProcessAsync(UploadBytes.Text, AttachmentContentTypes.Markdown);

        Assert.Equal(new AssistantFileResult.Rejected("Filen kunne ikke lagres. Prøv igjen."), result);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_cancelled_while_saving_is_not_saved_and_leaves_nothing_on_disk()
    {
        _repository.HoldSave();
        using var cancellation = new CancellationTokenSource();
        var processing = ProcessAsync(UploadBytes.Text, AttachmentContentTypes.Markdown, cancellation.Token);
        await _repository.WaitUntilSaveStartedAsync();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Empty(_repository.Saved);
        Assert.Empty(_environment.FilesOnDisk);
    }
}
