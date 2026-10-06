using kisatsingen.Services.Chat;
using kisatsingen.Services.Attachments;
using kisatsingen.Tests.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatAttachmentsTests : IDisposable
{
    private readonly AttachmentTestEnvironment _environment = new();
    private readonly ChatAttachments _attachments;

    public ChatAttachmentsTests() =>
        _attachments = _environment.CreateChatAttachments(new FakeAuthenticationService());

    public void Dispose()
    {
        _attachments.Dispose();
        _environment.Dispose();
    }

    private static InputFileChangeEventArgs Selecting(params IBrowserFile[] files) => new(files);

    [Fact]
    public async Task A_picked_file_becomes_a_ready_attachment_backed_by_a_temp_file()
    {
        await _attachments.UploadAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(_attachments.Pending).Status);
        Assert.Single(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Every_file_in_a_selection_is_uploaded()
    {
        await _attachments.UploadAsync(Selecting(
            new FakeBrowserFile("a.md", UploadBytes.Text),
            new FakeBrowserFile("b.png", UploadBytes.Png)));

        Assert.All(_attachments.Pending, attachment => Assert.Equal(AttachmentStatus.Ready, attachment.Status));
    }

    [Fact]
    public async Task A_selection_over_the_limit_is_refused_as_a_whole_before_any_file_is_read()
    {
        var files = Enumerable.Range(0, _environment.Options.MaxFilesPerSelection + 1)
            .Select(index => (IBrowserFile)new FakeBrowserFile($"{index}.md", UploadBytes.Text))
            .ToArray();

        await _attachments.UploadAsync(Selecting(files));

        Assert.NotNull(_attachments.SelectionNotice);
        Assert.Empty(_attachments.Pending);
    }

    [Fact]
    public async Task A_renamed_file_is_listed_as_refused_and_leaves_nothing_on_disk()
    {
        await _attachments.UploadAsync(Selecting(new FakeBrowserFile("bilde.png", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Rejected, Assert.Single(_attachments.Pending).Status);
        Assert.Empty(_environment.FilesOnDisk);
    }

    // The declared size is the client's claim; the bytes are what count.
    [Fact]
    public async Task A_file_that_sends_more_than_the_limit_is_refused_whatever_size_it_declared()
    {
        var content = new byte[_environment.Options.MaxFileBytes + 1];
        var liar = new FakeBrowserFile("notat.txt", () => new MemoryStream(content), size: 10);

        await _attachments.UploadAsync(Selecting(liar));

        Assert.Equal(AttachmentStatus.Rejected, Assert.Single(_attachments.Pending).Status);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_connection_lost_mid_upload_is_listed_as_interrupted_and_leaves_nothing_on_disk()
    {
        var breaking = new FakeBrowserFile("notat.md", () => throw new IOException("connection reset"), UploadBytes.Text.Length);

        await _attachments.UploadAsync(Selecting(breaking));

        Assert.Equal(UploadRejections.Interrupted, Assert.Single(_attachments.Pending).RejectionReason);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task An_attachment_counts_as_uploading_until_its_bytes_have_arrived()
    {
        var reached = new TaskCompletionSource();
        var stalling = new FakeBrowserFile("notat.md", () => new StallingStream(UploadBytes.Text, reached), 1024);
        var upload = _attachments.UploadAsync(Selecting(stalling));
        await reached.Task;

        var isUploading = _attachments.IsUploading;

        _attachments.Reset();
        await upload;
        Assert.True(isUploading);
    }

    [Fact]
    public async Task Removing_an_attachment_mid_upload_stops_it_and_leaves_nothing_on_disk()
    {
        var reached = new TaskCompletionSource();
        var stalling = new FakeBrowserFile("notat.md", () => new StallingStream(UploadBytes.Text, reached), 1024);
        var upload = _attachments.UploadAsync(Selecting(stalling));
        await reached.Task;

        _attachments.Remove(Assert.Single(_attachments.Pending).UploadId);
        await upload;

        Assert.Empty(_attachments.Pending);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Removing_a_ready_attachment_deletes_its_temp_file()
    {
        await _attachments.UploadAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Remove(Assert.Single(_attachments.Pending).UploadId);

        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Resetting_for_another_chat_discards_what_was_attached()
    {
        await _attachments.UploadAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Reset();

        Assert.Empty(_attachments.Pending);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_closed_circuit_leaves_no_temp_files_behind()
    {
        await _attachments.UploadAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Dispose();

        Assert.Empty(_environment.FilesOnDisk);
    }
}
