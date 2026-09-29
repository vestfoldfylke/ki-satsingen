using System.Buffers.Binary;
using kisatsingen.Services.Attachments;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// Long, not int: a PNG header can claim up to 2^32 − 1 pixels per side, and
// the point of reading it is to refuse such claims, not to overflow on them.
public sealed record ImageDimensions(long Width, long Height);

// Width and height from the header alone, with no image library, so a size
// limit can be checked before anything is decoded. Null means the header is
// malformed, which is itself a reason to refuse the file.
internal static class ImageHeader
{
    // Signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4).
    private const int PngHeaderLength = 24;
    private static readonly byte[] PngIhdr = "IHDR"u8.ToArray();

    private const byte JpegMarkerPrefix = 0xFF;
    private const byte JpegStartOfScan = 0xDA;
    private const byte JpegEndOfImage = 0xD9;

    // Bounds the scan of a JPEG crafted to hold nothing but tiny segments.
    private const int MaxJpegSegments = 1_000;

    public static async Task<ImageDimensions?> ReadAsync(Stream image, string contentType, CancellationToken ct)
    {
        if (string.Equals(contentType, AttachmentContentTypes.Png.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return await ReadPngAsync(image, ct);
        }

        if (string.Equals(contentType, AttachmentContentTypes.Jpeg.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return await ReadJpegAsync(image, ct);
        }

        throw new ArgumentException($"ImageHeader cannot read '{contentType}'. Only PNG and JPEG are supported.", nameof(contentType));
    }

    private static async Task<ImageDimensions?> ReadPngAsync(Stream image, CancellationToken ct)
    {
        var header = new byte[PngHeaderLength];
        if (await image.ReadAtLeastAsync(header, PngHeaderLength, throwOnEndOfStream: false, ct) < PngHeaderLength)
        {
            return null;
        }

        // The first chunk must be IHDR; its position is fixed by the format.
        if (!header.AsSpan(12, 4).SequenceEqual(PngIhdr))
        {
            return null;
        }

        return new ImageDimensions(
            BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4)));
    }

    // Walks the segments after the start-of-image marker until a start-of-frame
    // segment, which carries the size. Reads past everything else rather than
    // seeking, so any stream works, including one that cannot seek. Frame
    // headers come early, so this reads the metadata segments (EXIF can be
    // 64 KB), not the image.
    private static async Task<ImageDimensions?> ReadJpegAsync(Stream image, CancellationToken ct)
    {
        var buffer = new byte[7];
        byte[]? skipBuffer = null;

        // Start of image: FF D8.
        if (await image.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, throwOnEndOfStream: false, ct) < 2 || buffer[0] != JpegMarkerPrefix || buffer[1] != 0xD8)
        {
            return null;
        }

        for (var segment = 0; segment < MaxJpegSegments; segment++)
        {
            var marker = await ReadMarkerAsync(image, buffer, ct);
            if (marker is null or JpegStartOfScan or JpegEndOfImage)
            {
                // Image data or the end came before any frame header.
                return null;
            }

            if (IsStandalone(marker.Value))
            {
                continue;
            }

            if (await image.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, throwOnEndOfStream: false, ct) < 2)
            {
                return null;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, 2));
            if (segmentLength < 2)
            {
                return null;
            }

            if (IsStartOfFrame(marker.Value))
            {
                // Precision (1), height (2), width (2).
                if (await image.ReadAtLeastAsync(buffer.AsMemory(0, 5), 5, throwOnEndOfStream: false, ct) < 5)
                {
                    return null;
                }

                return new ImageDimensions(
                    Width: BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(3, 2)),
                    Height: BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1, 2)));
            }

            // A segment's length field is 16 bits, so one buffer of that size
            // skips any segment in a single read.
            skipBuffer ??= new byte[ushort.MaxValue];
            var remaining = segmentLength - 2;
            if (await image.ReadAtLeastAsync(skipBuffer.AsMemory(0, remaining), remaining, throwOnEndOfStream: false, ct) < remaining)
            {
                // The segment claims more bytes than the file has.
                return null;
            }
        }

        return null;
    }

    // A marker is FF followed by its code; any number of extra FF bytes may
    // pad it, per the format.
    private static async Task<byte?> ReadMarkerAsync(Stream image, byte[] buffer, CancellationToken ct)
    {
        if (await image.ReadAtLeastAsync(buffer.AsMemory(0, 1), 1, throwOnEndOfStream: false, ct) < 1 || buffer[0] != JpegMarkerPrefix)
        {
            return null;
        }

        do
        {
            if (await image.ReadAtLeastAsync(buffer.AsMemory(0, 1), 1, throwOnEndOfStream: false, ct) < 1)
            {
                return null;
            }
        }
        while (buffer[0] == JpegMarkerPrefix);

        return buffer[0];
    }

    // Markers with no length field: restart markers, TEM and a repeated SOI.
    private static bool IsStandalone(byte marker) =>
        marker is 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7);

    // C0–CF are frame headers, except DHT (C4), JPG (C8) and DAC (CC), which
    // share the range but carry tables.
    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
}
