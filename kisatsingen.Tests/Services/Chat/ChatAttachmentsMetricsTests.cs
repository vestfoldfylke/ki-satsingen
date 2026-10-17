using kisatsingen.Constants;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;
using kisatsingen.Tests.Services.Attachments;
using kisatsingen.Tests.Services.KnowledgeFiles.Pipeline;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatAttachmentsMetricsTests : IDisposable
{
    private const string RejectedCounter = "_attachment_rejected_total";
    private const string UploadedBytesCounter = "_attachment_uploaded_bytes_total";

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly ChatAttachments _attachments;

    public ChatAttachmentsMetricsTests() =>
        _attachments = _environment.CreateChatAttachments(new FakeAuthenticationService());

    public void Dispose()
    {
        _attachments.Dispose();
        _environment.Dispose();
    }

    private static InputFileChangeEventArgs Selecting(params IBrowserFile[] files) => new(files);

    private IEnumerable<string> RejectionReasons() =>
        _environment.Metrics.Named(RejectedCounter).Select(call => call.Label(MetricConstants.MetricsReasonLabelName));

    [Fact]
    public async Task A_refused_file_is_counted_by_its_rejection_key()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("bilde.png", UploadBytes.Text)));

        Assert.Equal(["wrong_file_type"], RejectionReasons());
    }

    [Fact]
    public async Task A_refused_file_is_still_counted_when_its_chat_is_reset_before_the_selection_ends()
    {
        var reached = new TaskCompletionSource();
        var stalling = new FakeBrowserFile("notat.md", () => new StallingStream(UploadBytes.Text, reached), 1024);
        var upload = _attachments.AttachAsync(Selecting(new FakeBrowserFile("program.exe", UploadBytes.Text), stalling));
        await reached.Task;

        _attachments.Reset();
        await upload;

        Assert.Equal(["unsupported_type"], RejectionReasons());
    }

    // Its bytes moved before it was found to be a copy.
    [Fact]
    public async Task A_duplicate_still_records_its_bytes()
    {
        await _attachments.AttachAsync(Selecting(
            new FakeBrowserFile("notat.md", UploadBytes.Text),
            new FakeBrowserFile("notat-kopi.md", UploadBytes.Text)));

        Assert.Equal(2, _environment.Metrics.Named(UploadedBytesCounter).Count);
    }

    // An exception here would end the circuit.
    [Fact]
    public async Task A_metrics_failure_does_not_fail_the_attachment()
    {
        using var attachments = _environment.CreateChatAttachments(new FakeAuthenticationService(), new BrokenMetricsService());

        await attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(attachments.All).Status);
    }
}
