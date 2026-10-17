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
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(_attachments.All).Status);
        Assert.Single(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Every_file_in_a_selection_is_uploaded()
    {
        await _attachments.AttachAsync(Selecting(
            new FakeBrowserFile("a.md", UploadBytes.Text),
            new FakeBrowserFile("b.png", UploadBytes.Png)));

        Assert.All(_attachments.All, attachment => Assert.Equal(AttachmentStatus.Ready, attachment.Status));
    }

    [Fact]
    public async Task A_selection_over_the_limit_is_refused_as_a_whole_before_any_file_is_read()
    {
        var files = Enumerable.Range(0, AttachmentOptions.MaxFilesPerSelection + 1)
            .Select(index => (IBrowserFile)new FakeBrowserFile($"{index}.md", UploadBytes.Text))
            .ToArray();

        await _attachments.AttachAsync(Selecting(files));

        Assert.NotNull(_attachments.SelectionNotice);
        Assert.Empty(_attachments.All);
    }

    [Fact]
    public async Task A_renamed_file_is_listed_as_refused_and_leaves_nothing_on_disk()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("bilde.png", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Rejected, Assert.Single(_attachments.All).Status);
        Assert.Empty(_environment.FilesOnDisk);
    }

    // The declared size is the client's claim; the bytes are what count.
    [Fact]
    public async Task A_file_that_sends_more_than_the_limit_is_refused_whatever_size_it_declared()
    {
        var content = new byte[_environment.Options.MaxFileBytes + 1];
        var liar = new FakeBrowserFile("notat.txt", () => new MemoryStream(content), size: 10);

        await _attachments.AttachAsync(Selecting(liar));

        Assert.Equal(AttachmentStatus.Rejected, Assert.Single(_attachments.All).Status);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_connection_lost_mid_upload_is_listed_as_interrupted_and_leaves_nothing_on_disk()
    {
        var breaking = new FakeBrowserFile("notat.md", () => throw new IOException("connection reset"), UploadBytes.Text.Length);

        await _attachments.AttachAsync(Selecting(breaking));

        Assert.Equal(UploadRejections.Interrupted, Assert.Single(_attachments.All).Rejection);
        Assert.Empty(_environment.FilesOnDisk);
    }

    // An exception here would end the circuit.
    [Fact]
    public async Task A_file_picked_after_the_attachment_folder_is_gone_is_listed_as_unavailable()
    {
        Directory.Delete(_environment.Folder, recursive: true);

        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(UploadRejections.Unavailable, Assert.Single(_attachments.All).Rejection);
    }

    [Fact]
    public async Task An_attachment_counts_as_uploading_until_its_bytes_have_arrived()
    {
        var reached = new TaskCompletionSource();
        var stalling = new FakeBrowserFile("notat.md", () => new StallingStream(UploadBytes.Text, reached), 1024);
        var upload = _attachments.AttachAsync(Selecting(stalling));
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
        var upload = _attachments.AttachAsync(Selecting(stalling));
        await reached.Task;

        _attachments.Remove(Assert.Single(_attachments.All).AttachmentId);
        await upload;

        Assert.Empty(_attachments.All);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Removing_a_ready_attachment_deletes_its_temp_file()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Remove(Assert.Single(_attachments.All).AttachmentId);

        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task Resetting_for_another_chat_discards_what_was_attached()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Reset();

        Assert.Empty(_attachments.All);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_closed_circuit_leaves_no_temp_files_behind()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        _attachments.Dispose();

        Assert.Empty(_environment.FilesOnDisk);
    }

    // Only a tampered browser declares one.
    [Fact]
    public async Task A_file_declaring_a_negative_size_is_refused_before_any_bytes_are_read()
    {
        var tampered = new FakeBrowserFile("notat.md", () => new MemoryStream(UploadBytes.Text), size: -1);

        await _attachments.AttachAsync(Selecting(tampered));

        Assert.Equal(UploadRejections.UnreadableSize, Assert.Single(_attachments.All).Rejection);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_declared_over_the_size_limit_is_refused_before_any_bytes_are_read()
    {
        var tooLarge = new FakeBrowserFile("notat.md", () => new MemoryStream(UploadBytes.Text), size: _environment.Options.MaxFileBytes + 1);

        await _attachments.AttachAsync(Selecting(tooLarge));

        Assert.Equal(UploadRejections.TooLarge(_environment.Options.MaxFileBytes), Assert.Single(_attachments.All).Rejection);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_over_the_pending_file_limit_is_refused_when_its_upload_starts()
    {
        using var environment = new AttachmentTestEnvironment(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        using var attachments = environment.CreateChatAttachments(new FakeAuthenticationService());

        await attachments.AttachAsync(Selecting(
            new FakeBrowserFile("a.md", UploadBytes.Text),
            new FakeBrowserFile("b.md", UploadBytes.Png)));

        Assert.Equal(
            [(AttachmentStatus.Ready, null), (AttachmentStatus.Rejected, UploadRejections.TooManyPending(1))],
            attachments.All.Select(attachment => (attachment.Status, attachment.Rejection)));
    }

    [Fact]
    public async Task Removing_a_ready_attachment_frees_its_room_in_the_quota()
    {
        using var environment = new AttachmentTestEnvironment(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        using var attachments = environment.CreateChatAttachments(new FakeAuthenticationService());
        await attachments.AttachAsync(Selecting(new FakeBrowserFile("a.md", UploadBytes.Text)));
        attachments.Remove(Assert.Single(attachments.All).AttachmentId);

        await attachments.AttachAsync(Selecting(new FakeBrowserFile("b.md", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(attachments.All).Status);
    }

    [Fact]
    public async Task Content_whose_earlier_copy_was_removed_is_accepted_again()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));
        _attachments.Remove(Assert.Single(_attachments.All).AttachmentId);

        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(AttachmentStatus.Ready, Assert.Single(_attachments.All).Status);
    }

    [Fact]
    public async Task A_new_selection_clears_the_refusals_of_the_one_before()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("program.exe", UploadBytes.Text)));

        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));

        Assert.Equal(["notat.md"], _attachments.All.Select(attachment => attachment.FileName));
    }

    [Fact]
    public async Task A_file_taken_at_send_survives_the_composer_being_reset()
    {
        await _attachments.AttachAsync(Selecting(new FakeBrowserFile("notat.md", UploadBytes.Text)));
        using var sentAttachments = _attachments.TakeReady();

        _attachments.Reset();

        Assert.True(File.Exists(Assert.Single(sentAttachments.All).File.Path));
    }

    // From send the turn owns the file; it is still on disk until the turn
    // releases it, so it still counts.
    [Fact]
    public async Task A_file_taken_at_send_leaves_the_composer_but_counts_until_it_is_released()
    {
        using var environment = new AttachmentTestEnvironment(new AttachmentOptions { MaxPendingFilesPerUser = 1 });
        using var attachments = environment.CreateChatAttachments(new FakeAuthenticationService());
        await attachments.AttachAsync(Selecting(new FakeBrowserFile("a.md", UploadBytes.Text)));
        var sentAttachments = attachments.TakeReady();

        await attachments.AttachAsync(Selecting(new FakeBrowserFile("b.md", UploadBytes.Png)));
        var whileTaken = Assert.Single(attachments.All).Status;
        sentAttachments.Dispose();
        await attachments.AttachAsync(Selecting(new FakeBrowserFile("c.md", UploadBytes.Png)));

        Assert.Equal(AttachmentStatus.Rejected, whileTaken);
        Assert.Equal(AttachmentStatus.Ready, Assert.Single(attachments.All).Status);
    }
}
