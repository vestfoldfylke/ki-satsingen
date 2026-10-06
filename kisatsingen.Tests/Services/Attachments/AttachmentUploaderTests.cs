using System.Security.Cryptography;
using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentUploaderTests : IDisposable
{
    private readonly AttachmentTestEnvironment _environment = new();
    private static readonly IProgress<long> IgnoredProgress = new Progress<long>();

    public void Dispose() => _environment.Dispose();

    private Task<UploadOutcome> CopyAsync(Stream source, AttachmentType type, long maxBytes = 1024, CancellationToken ct = default) =>
        _environment.Uploader.CopyAsync(source, type, maxBytes, IgnoredProgress, ct);

    [Fact]
    public async Task A_stored_file_holds_exactly_the_uploaded_bytes()
    {
        var outcome = await CopyAsync(new MemoryStream(UploadBytes.Text), AttachmentContentTypes.PlainText);

        var stored = Assert.IsType<UploadOutcome.Stored>(outcome);
        Assert.Equal(UploadBytes.Text, await File.ReadAllBytesAsync(stored.File.Path));
    }

    [Fact]
    public async Task The_hash_is_the_sha256_of_the_uploaded_bytes()
    {
        var outcome = await CopyAsync(new MemoryStream(UploadBytes.Text), AttachmentContentTypes.PlainText);

        var stored = Assert.IsType<UploadOutcome.Stored>(outcome);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(UploadBytes.Text)), stored.Sha256);
    }

    // The declared size is the client's claim; the bytes are counted regardless.
    [Fact]
    public async Task A_file_over_the_limit_is_rejected_and_leaves_nothing_on_disk()
    {
        var outcome = await CopyAsync(new MemoryStream(new byte[2048]), AttachmentContentTypes.PlainText, maxBytes: 1024);

        Assert.IsType<UploadOutcome.Rejected>(outcome);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_file_whose_bytes_do_not_match_its_image_type_is_rejected_and_leaves_nothing_on_disk()
    {
        var outcome = await CopyAsync(new MemoryStream(UploadBytes.Text), AttachmentContentTypes.Png);

        Assert.Equal(new UploadOutcome.Rejected(UploadRejections.WrongSignature), outcome);
        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public async Task A_signature_that_arrives_split_across_reads_is_still_recognised()
    {
        var outcome = await CopyAsync(new TricklingStream(UploadBytes.Png), AttachmentContentTypes.Png);

        Assert.IsType<UploadOutcome.Stored>(outcome);
    }

    [Fact]
    public async Task A_file_shorter_than_its_types_signature_is_rejected()
    {
        var outcome = await CopyAsync(new MemoryStream(UploadBytes.Png[..3]), AttachmentContentTypes.Png);

        Assert.Equal(new UploadOutcome.Rejected(UploadRejections.WrongSignature), outcome);
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        var outcome = await CopyAsync(new MemoryStream(), AttachmentContentTypes.PlainText);

        Assert.Equal(new UploadOutcome.Rejected(UploadRejections.Empty), outcome);
    }

    [Fact]
    public async Task A_cancelled_copy_leaves_nothing_on_disk()
    {
        var reached = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();
        var copy = CopyAsync(new StallingStream(UploadBytes.Text, reached), AttachmentContentTypes.PlainText, ct: cancellation.Token);
        await reached.Task;

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
        Assert.Empty(_environment.FilesOnDisk);
    }
}
