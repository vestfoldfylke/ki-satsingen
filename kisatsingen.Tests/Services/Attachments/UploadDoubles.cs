using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Tests.Services.Attachments;

// What InputFile hands over. Size is settable apart from the content, because
// the declared size is the client's claim and tests need to lie with it.
internal sealed class FakeBrowserFile(string name, Func<Stream> open, long size) : IBrowserFile
{
    public FakeBrowserFile(string name, byte[] content) : this(name, () => new MemoryStream(content), content.Length)
    {
    }

    public string Name => name;
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => size;
    public string ContentType => "application/octet-stream";

    // Mirrors the real stream, which refuses a file declared over the limit.
    public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken ct = default) =>
        size > maxAllowedSize
            ? throw new IOException($"Supplied file with size {size} bytes exceeds the maximum of {maxAllowedSize} bytes.")
            : open();
}

// Hands out one byte per read, the way a slow connection can.
internal sealed class TricklingStream(byte[] content) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => content.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= content.Length || count == 0)
        {
            return 0;
        }

        buffer[offset] = content[_position++];
        return 1;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_position >= content.Length || buffer.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        buffer.Span[0] = content[_position++];
        return ValueTask.FromResult(1);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Delivers its first bytes, then waits until cancelled: an upload genuinely
// in flight.
internal sealed class StallingStream(byte[] first, TaskCompletionSource reached) : Stream
{
    private bool _hasDeliveredFirst;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!_hasDeliveredFirst)
        {
            _hasDeliveredFirst = true;
            first.CopyTo(buffer);
            return first.Length;
        }

        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Starts at the real clock, so file timestamps and registry times agree.
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class UploadBytes
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    public static readonly byte[] Text = "Hei, dette er en notatfil.\n"u8.ToArray();
}
