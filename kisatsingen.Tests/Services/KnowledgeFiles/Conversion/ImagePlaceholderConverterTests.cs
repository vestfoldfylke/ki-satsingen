using System.Buffers.Binary;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class ImagePlaceholderConverterTests
{
    private const long MaxSide = 8_000;
    private const long MaxPixels = 40_000_000;

    private static Task<ConversionResult> ConvertAsync(byte[] image, AttachmentType type) =>
        ConvertAsync(new MemoryStream(image), type);

    private static Task<ConversionResult> ConvertAsync(Stream image, AttachmentType type) =>
        new ImagePlaceholderConverter(MaxSide, MaxPixels).ConvertAsync(
            new ConversionRequest("bilde", type.ContentType, image, MaxEstimatedTokens: 1_000),
            CancellationToken.None);

    private static byte[] Png(uint width, uint height)
    {
        var header = new byte[33];
        AttachmentContentTypes.Png.Signature!.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), height);
        return header;
    }

    // SOI, an APP0 segment to skip, then a baseline frame header. Fill bytes
    // before the frame marker are allowed by the format.
    private static byte[] Jpeg(ushort width, ushort height)
    {
        byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, .. new byte[14]];
        byte[] frame = [0xFF, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, .. new byte[10]];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), width);
        return [0xFF, 0xD8, .. app0, .. frame];
    }

    [Fact]
    public async Task A_png_becomes_placeholder_markdown_carrying_its_size()
    {
        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        var converted = Assert.IsType<ConversionResult.Converted>(result);
        Assert.Equal(("Bilde: 1024 x 768 px. Innholdet er ikke tolket ennå.", ContentOrigin.ImagePlaceholder), (converted.Markdown, converted.Origin));
    }

    [Fact]
    public async Task A_jpeg_frame_header_is_found_past_the_segments_before_it()
    {
        var result = await ConvertAsync(Jpeg(640, 480), AttachmentContentTypes.Jpeg);

        Assert.Contains("640 x 480", Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    [Fact]
    public async Task An_image_wider_than_the_side_limit_is_refused()
    {
        var result = await ConvertAsync(Png(9_000, 100), AttachmentContentTypes.Png);

        Assert.IsType<ConversionResult.Rejected>(result);
    }

    // Within the side limit both ways, but a pixel bomb by area.
    [Fact]
    public async Task An_image_over_the_total_pixel_limit_is_refused()
    {
        var result = await ConvertAsync(Png(7_000, 7_000), AttachmentContentTypes.Png);

        Assert.IsType<ConversionResult.Rejected>(result);
    }

    [Fact]
    public async Task An_image_claiming_a_side_of_zero_is_refused_as_malformed()
    {
        var result = await ConvertAsync(Png(0, 100), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.MalformedImage), result);
    }

    [Fact]
    public async Task A_png_cut_short_before_its_size_is_refused_as_malformed()
    {
        var result = await ConvertAsync(Png(100, 100)[..18], AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.MalformedImage), result);
    }

    // A remote converter or a network stream cannot seek; the header is still
    // readable front to back.
    [Fact]
    public async Task A_jpeg_is_read_from_a_stream_that_cannot_seek()
    {
        var result = await ConvertAsync(new ForwardOnlyStream(Jpeg(640, 480)), AttachmentContentTypes.Jpeg);

        Assert.Contains("640 x 480", Assert.IsType<ConversionResult.Converted>(result).Markdown);
    }

    [Fact]
    public async Task A_jpeg_segment_claiming_more_bytes_than_the_file_has_is_refused_as_malformed()
    {
        byte[] truncated = [0xFF, 0xD8, 0xFF, 0xE0, 0x10, 0x00, 0x01, 0x02];

        var result = await ConvertAsync(truncated, AttachmentContentTypes.Jpeg);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.MalformedImage), result);
    }

    [Fact]
    public async Task A_jpeg_whose_image_data_starts_before_any_frame_header_is_refused_as_malformed()
    {
        var result = await ConvertAsync([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02], AttachmentContentTypes.Jpeg);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.MalformedImage), result);
    }

    // Readable front to back and nothing else, like a network stream.
    private sealed class ForwardOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
