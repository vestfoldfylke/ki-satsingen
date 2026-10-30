using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Assistants;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Assistants;

// The real upload stack, quota (one file pending at a time) and pipeline; only
// the repository is fake.
public sealed class AssistantFileUploadsTests : IDisposable
{
    private static readonly Guid AssistantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly AttachmentTestEnvironment _environment = new(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
    private readonly FakeAssistantFileRepository _repository = new();
    private readonly AssistantFileUploads _uploads;

    public AssistantFileUploadsTests() =>
        _uploads = new AssistantFileUploads(
            new FakeAuthenticationService(),
            _environment.Uploader,
            _environment.Quota,
            CreatePipeline(),
            _repository,
            new AttachmentMetrics(_environment.Metrics, NullLogger<AttachmentMetrics>.Instance),
            NullLogger<AssistantFileUploads>.Instance);

    public void Dispose()
    {
        _uploads.Dispose();
        _environment.Dispose();
    }

    private KnowledgeFilePipeline CreatePipeline() =>
        new(
            new DocumentConverterRegistry([new TextDocumentConverter()]),
            new OpeningExcerptSummarizer(),
            _environment.Storage,
            new KnowledgeFilePipelineOptions(),
            new KnowledgeFilePipelineMetrics(new RecordingMetricsService(), NullLogger<KnowledgeFilePipelineMetrics>.Instance),
            maxEstimatedTokens: 1_000_000,
            NullLogger<KnowledgeFilePipeline>.Instance);

    private Task AddAsync(IBrowserFile[] files, int savedFileCount = 0, Func<Task>? onFileSaved = null) =>
        _uploads.AddAsync(AssistantId, new InputFileChangeEventArgs(files), savedFileCount, onFileSaved ?? (() => Task.CompletedTask));

    private static FakeBrowserFile TextFile(string name) => new(name, UploadBytes.Text);

    private static FakeBrowserFile TextFile(string name, Action onOpened) =>
        new(name, () =>
        {
            onOpened();
            return new MemoryStream(UploadBytes.Text);
        }, UploadBytes.Text.Length);

    private static FakeBrowserFile StallingFile(string name, TaskCompletionSource reached) =>
        new(name, () => new StallingStream(UploadBytes.Text, reached), 1024);

    [Fact]
    public async Task Every_file_in_a_selection_is_saved_one_after_another_and_reported_as_each_is_saved()
    {
        var savedCountAtEachReport = new List<int>();

        await AddAsync(
            [TextFile("a.md"), TextFile("b.md")],
            onFileSaved: () =>
            {
                savedCountAtEachReport.Add(_repository.Saved.Count);
                return Task.CompletedTask;
            });

        Assert.Equal([1, 2], savedCountAtEachReport);
        Assert.Empty(_uploads.All);
    }

    [Fact]
    public async Task A_selection_over_the_limit_is_refused_as_a_whole_before_any_file_is_read()
    {
        var files = Enumerable.Range(0, AttachmentOptions.MaxFilesPerSelection + 1)
            .Select(index => (IBrowserFile)TextFile($"{index}.md"))
            .ToArray();

        await AddAsync(files);

        Assert.NotNull(_uploads.SelectionNotice);
        Assert.Empty(_uploads.All);
        Assert.Empty(_repository.Saved);
    }

    [Fact]
    public async Task A_file_of_a_type_not_allowed_is_listed_with_its_reason_and_never_uploaded()
    {
        var isOpened = false;

        await AddAsync([TextFile("program.exe", onOpened: () => isOpened = true)]);

        Assert.Equal(UploadRejections.UnsupportedType.Message, Assert.Single(_uploads.All).RejectionReason);
        Assert.False(isOpened);
    }

    [Fact]
    public async Task A_file_of_a_type_not_allowed_is_counted_by_its_rejection_key()
    {
        await AddAsync([TextFile("program.exe")]);

        var rejection = Assert.Single(_environment.Metrics.Named("_attachment_rejected_total"));
        Assert.Equal("unsupported_type", rejection.Label(MetricConstants.MetricsReasonLabelName));
    }

    [Fact]
    public async Task Files_beyond_the_room_left_in_the_assistant_are_refused_as_full_while_the_rest_are_saved()
    {
        await AddAsync([TextFile("a.md"), TextFile("b.md")], savedFileCount: Assistant.MaxKnowledgeFiles - 1);

        Assert.Equal("a.md", Assert.Single(_repository.Saved).File.FileName);
        Assert.Equal(KnowledgeFileSaveRejections.AssistantFull, Assert.Single(_uploads.All).RejectionReason);
    }

    [Fact]
    public async Task A_file_with_no_room_left_in_the_quota_is_rejected_before_it_uploads()
    {
        var (otherFile, stream) = _environment.Storage.CreateFile(ChatSessionHarness.OwnerUnderTest, Guid.NewGuid());
        await stream.DisposeAsync();
        _environment.Storage.TryMarkReady(otherFile);
        var isOpened = false;

        await AddAsync([TextFile("notat.md", onOpened: () => isOpened = true)]);

        Assert.Equal(UploadRejections.TooManyPending(1).Message, Assert.Single(_uploads.All).RejectionReason);
        Assert.False(isOpened);
    }

    [Fact]
    public async Task A_file_whose_content_is_not_its_type_is_listed_as_the_wrong_type()
    {
        await AddAsync([TextFile("bilde.png")]);

        Assert.Equal(UploadRejections.WrongFileType.Message, Assert.Single(_uploads.All).RejectionReason);
    }

    [Fact]
    public async Task A_file_whose_content_is_not_its_type_is_counted_by_its_rejection_key()
    {
        await AddAsync([TextFile("bilde.png")]);

        var rejection = Assert.Single(_environment.Metrics.Named("_attachment_rejected_total"));
        Assert.Equal("wrong_file_type", rejection.Label(MetricConstants.MetricsReasonLabelName));
    }

    [Fact]
    public async Task A_file_the_processor_rejects_is_listed_with_its_reason()
    {
        _repository.RejectionReason = KnowledgeFileSaveRejections.DuplicateInAssistant;

        await AddAsync([TextFile("notat.md")]);

        var upload = Assert.Single(_uploads.All);
        Assert.Equal(AssistantFileUploadStatus.Rejected, upload.Status);
        Assert.Equal(KnowledgeFileSaveRejections.DuplicateInAssistant, upload.RejectionReason);
    }

    [Fact]
    public async Task A_new_selection_clears_the_refusals_of_the_one_before()
    {
        await AddAsync([TextFile("program.exe")]);

        await AddAsync([TextFile("notat.md")]);

        Assert.Empty(_uploads.All);
    }

    [Fact]
    public async Task A_dismissed_refusal_leaves_the_list()
    {
        await AddAsync([TextFile("program.exe")]);

        _uploads.Dismiss(Assert.Single(_uploads.All).Id);

        Assert.Empty(_uploads.All);
    }

    [Fact]
    public async Task A_file_in_progress_cannot_be_dismissed()
    {
        var reached = new TaskCompletionSource();
        var adding = AddAsync([StallingFile("notat.md", reached)]);
        await reached.Task;

        _uploads.Dismiss(Assert.Single(_uploads.All).Id);

        var listedCount = _uploads.All.Count;
        _uploads.Reset();
        await adding;
        Assert.Equal(1, listedCount);
    }

    [Fact]
    public async Task A_file_counts_as_uploading_while_its_bytes_arrive()
    {
        var reached = new TaskCompletionSource();
        var adding = AddAsync([StallingFile("notat.md", reached)]);
        await reached.Task;

        var isUploading = _uploads.IsUploading;

        _uploads.Reset();
        await adding;
        Assert.True(isUploading);
    }

    [Fact]
    public async Task A_file_is_processing_while_it_is_saved()
    {
        AssistantFileUploadStatus? statusWhileSaving = null;
        _repository.OnSave = () => statusWhileSaving = Assert.Single(_uploads.All).Status;

        await AddAsync([TextFile("notat.md")]);

        Assert.Equal(AssistantFileUploadStatus.Processing, statusWhileSaving);
    }

    // So the file never disappears from the page between the two lists.
    [Fact]
    public async Task A_saved_file_stays_listed_until_the_saved_files_are_reloaded()
    {
        var listedCountWhileReloading = -1;

        await AddAsync(
            [TextFile("notat.md")],
            onFileSaved: () =>
            {
                listedCountWhileReloading = _uploads.All.Count;
                return Task.CompletedTask;
            });

        Assert.Equal(1, listedCountWhileReloading);
    }

    [Fact]
    public async Task Resetting_mid_upload_clears_the_list_saves_nothing_leaves_nothing_on_disk_and_starts_no_later_file()
    {
        var reached = new TaskCompletionSource();
        var isLaterFileOpened = false;
        var adding = AddAsync([StallingFile("a.md", reached), TextFile("b.md", onOpened: () => isLaterFileOpened = true)]);
        await reached.Task;

        _uploads.Reset();
        await adding;

        Assert.Empty(_uploads.All);
        Assert.Empty(_repository.Saved);
        Assert.Empty(_environment.FilesOnDisk);
        Assert.False(isLaterFileOpened);
    }

    [Fact]
    public async Task A_reset_while_the_saved_files_are_reloaded_starts_no_later_file()
    {
        var isLaterFileOpened = false;

        await AddAsync(
            [TextFile("a.md"), TextFile("b.md", onOpened: () => isLaterFileOpened = true)],
            onFileSaved: () =>
            {
                _uploads.Reset();
                return Task.CompletedTask;
            });

        Assert.False(isLaterFileOpened);
    }

    [Fact]
    public async Task A_selection_after_a_reset_is_added()
    {
        _uploads.Reset();

        await AddAsync([TextFile("notat.md")]);

        Assert.Single(_repository.Saved);
    }
}
