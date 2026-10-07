using System.Buffers;
using System.Security.Cryptography;

namespace kisatsingen.Services.Attachments;

public abstract record UploadOutcome
{
    public sealed record Stored(TempFile File, long SizeBytes, string Sha256) : UploadOutcome;

    // Shown to the user as is.
    public sealed record Rejected(string Reason) : UploadOutcome;
}

// Copies an upload to a temp file in one pass: hashing, counting against the
// size limit and checking the type's signature as the bytes go by. Never holds
// more than one buffer of the file in memory.
public sealed class AttachmentUploader(TempFileStore store)
{
    // The default bufferSize documented for Stream.CopyToAsync. A tuning value,
    // not a correctness one: any positive size copies correctly, so change it
    // only after measuring uploads.
    private const int CopyBufferSize = 81_920;

    public async Task<UploadOutcome> CopyAsync(
        Stream source,
        AttachmentType type,
        long maxBytes,
        IProgress<long> progress,
        CancellationToken ct)
    {
        var (file, destination) = store.Create();
        UploadOutcome? outcome = null;

        try
        {
            outcome = await CopyToAsync(source, file, destination, type, maxBytes, progress, ct);
            return outcome;
        }
        finally
        {
            // Rejected, cancelled or failed: nothing may outlive the attempt.
            if (outcome is not UploadOutcome.Stored)
            {
                store.Delete(file);
            }
        }
    }

    // Owns the destination stream, so it is closed before CopyAsync deletes
    // the file.
    private static async Task<UploadOutcome> CopyToAsync(
        Stream source,
        TempFile file,
        FileStream destination,
        AttachmentType type,
        long maxBytes,
        IProgress<long> progress,
        CancellationToken ct)
    {
        await using var _ = destination;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            var signature = new SignatureCheck(type.Signature);
            long total = 0;

            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)) > 0)
            {
                total += read;

                // Counted here too, not left to OpenReadStream: the size the
                // browser declared is the client's claim, not a fact.
                if (total > maxBytes)
                {
                    return new UploadOutcome.Rejected(UploadRejections.TooLarge(maxBytes));
                }

                var chunk = buffer.AsSpan(0, read);
                if (!signature.Accepts(chunk))
                {
                    return new UploadOutcome.Rejected(UploadRejections.WrongSignature);
                }

                hash.AppendData(chunk);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                progress.Report(total);
            }

            if (total == 0)
            {
                return new UploadOutcome.Rejected(UploadRejections.Empty);
            }

            // A file shorter than its type's signature never proved its type.
            if (!signature.IsComplete)
            {
                return new UploadOutcome.Rejected(UploadRejections.WrongSignature);
            }

            return new UploadOutcome.Stored(file, total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Tracks the file's first bytes across reads, since one read may return
    // fewer bytes than the signature is long.
    private sealed class SignatureCheck(byte[]? expected)
    {
        private int _matched;

        public bool IsComplete => expected is null || _matched == expected.Length;

        public bool Accepts(ReadOnlySpan<byte> chunk)
        {
            if (IsComplete)
            {
                return true;
            }

            var remaining = expected!.AsSpan(_matched);
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
