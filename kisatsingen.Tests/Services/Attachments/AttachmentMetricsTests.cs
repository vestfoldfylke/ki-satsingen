using kisatsingen.Constants;
using kisatsingen.Services.Attachments;
using kisatsingen.Tests.Services.Chat;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentMetricsTests : IDisposable
{
    private const string FolderBytesGauge = "_attachment_folder_bytes";

    private readonly AttachmentTestEnvironment _environment = new();
    private readonly AttachmentMetrics _metrics;

    public AttachmentMetricsTests() =>
        _metrics = new AttachmentMetrics(_environment.Metrics, NullLogger<AttachmentMetrics>.Instance);

    public void Dispose() => _environment.Dispose();

    [Fact]
    public async Task The_folder_gauge_reports_a_ready_file_under_ready_and_nothing_under_uploading()
    {
        using var attachments = _environment.CreateChatAttachments(new FakeAuthenticationService());
        await attachments.AttachAsync(new InputFileChangeEventArgs([new FakeBrowserFile("notat.md", UploadBytes.Text)]));

        _metrics.RecordFolderUsage(_environment.Storage);

        var bytesByState = _environment.Metrics.Named(FolderBytesGauge)
            .ToDictionary(call => call.Label(MetricConstants.MetricsStateLabelName), call => call.Value);
        Assert.Equal(new Dictionary<string, double> { ["uploading"] = 0, ["ready"] = UploadBytes.Text.Length }, bytesByState);
    }

    // It runs before every scrape, where a throw would fail every metric.
    [Fact]
    public void A_missing_folder_does_not_throw()
    {
        Directory.Delete(_environment.Folder, recursive: true);

        var exception = Record.Exception(() => _metrics.RecordFolderUsage(_environment.Storage));

        Assert.Null(exception);
    }
}
