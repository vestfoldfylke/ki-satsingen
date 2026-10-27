using System.Buffers;
using System.Security.Cryptography;

namespace kisatsingen.Services.Attachments;

// Only the nested cases can derive, so an outcome is always Stored or Rejected.
public abstract record UploadOutcome
{
    private UploadOutcome() { }

    public sealed record Stored(TempFile File, long SizeBytes, string Sha256) : UploadOutcome;

    public sealed record Rejected(UploadRejection Reason) : UploadOutcome;
}

// Copies an upload to a temp file in one pass: hashing, counting against the
// size limit and checking the type's signature as the bytes go by. Never holds
// more than one buffer of the file in memory.
public sealed class AttachmentUploader(AttachmentStorage storage)
{
    // The default bufferSize documented for Stream.CopyToAsync. A tuning value,
    // not a correctness one: any positive size copies correctly, so change it
    // only after measuring uploads.
    private const int CopyBufferSize = 81_920;

    public async Task<UploadOutcome> UploadAsync(
        Stream uploadStream,
        Guid ownerId,
        Guid attachmentId,
        AttachmentType type,
        long maxBytes,
        IProgress<long> progress,
        CancellationToken ct)
    {
        var (tempFile, tempFileStream) = storage.CreateFile(ownerId, attachmentId);
        UploadOutcome? outcome = null;

        try
        {
            // Closed before the file is either returned or deleted below.
            await using (tempFileStream)
            {
                outcome = await CopyToTempFileAsync(uploadStream, tempFile, tempFileStream, type, maxBytes, progress, ct);
            }

            return outcome;
        }
        finally
        {
            // Rejected, cancelled or failed: nothing may outlive the attempt.
            if (outcome is not UploadOutcome.Stored)
            {
                tempFile.Dispose();
            }
        }
    }

    private static async Task<UploadOutcome> CopyToTempFileAsync(
        Stream uploadStream,
        TempFile tempFile,
        FileStream tempFileStream,
        AttachmentType type,
        long maxBytes,
        IProgress<long> progress,
        CancellationToken ct)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            var fileSignature = new FileSignature(type.Signature ?? []);
            long totalBytes = 0;

            int bytesRead;
            while ((bytesRead = await uploadStream.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)) > 0)
            {
                totalBytes += bytesRead;
                var chunk = buffer.AsSpan(0, bytesRead);

                // Counted here too, not left to OpenReadStream: the size the
                // browser declared is the client's claim, not a fact.
                if (totalBytes > maxBytes)
                {
                    return new UploadOutcome.Rejected(UploadRejections.TooLarge(maxBytes));
                }

                if (!fileSignature.MatchesNext(chunk))
                {
                    return new UploadOutcome.Rejected(UploadRejections.WrongFileType);
                }

                sha256.AppendData(chunk);
                await tempFileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                progress.Report(totalBytes);
            }

            if (totalBytes == 0)
            {
                return new UploadOutcome.Rejected(UploadRejections.Empty);
            }

            if (fileSignature.IsIncomplete)
            {
                return new UploadOutcome.Rejected(UploadRejections.WrongFileType);
            }

            return new UploadOutcome.Stored(tempFile, totalBytes, Convert.ToHexStringLower(sha256.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    // The bytes a file of its type must start with, as they arrive. Tracked
    // across reads, since one read may return fewer bytes than the signature
    // is long.
    private sealed class FileSignature(byte[] expected)
    {
        private int _matched;

        public bool IsIncomplete => _matched < expected.Length;

        public bool MatchesNext(ReadOnlySpan<byte> chunk)
        {
            if (!IsIncomplete)
            {
                return true;
            }

            var remaining = expected.AsSpan(_matched);
            var compared = Math.Min(remaining.Length, chunk.Length);
            if (!chunk[..compared].SequenceEqual(remaining[..compared]))
            {
                return false;
            }

            _matched += compared;
            return true;
        }
    }
}
