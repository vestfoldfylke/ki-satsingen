using System.Buffers.Binary;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.Chat;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion;

public sealed class ImageDocumentConverterTests
{
    private const string Description = "## Beskrivelse\nEt skjermbilde.\n\n## Tekst\nSak 2026/123";

    private const long MaxImageBytes = 1_000;

    private readonly FakeVisionClient _client = new();

    private ImageDocumentConverter Converter =>
        new(new ImageConversionModel("Mistral AI", "mistral-small-under-test", _client, new ChatOptions(), MaxImageBytes), NullLogger<ImageDocumentConverter>.Instance);

    private Task<ConversionResult> ConvertAsync(byte[] image, AttachmentType type, CancellationToken ct = default) =>
        ConvertAsync(new MemoryStream(image), type, ct);

    private Task<ConversionResult> ConvertAsync(Stream image, AttachmentType type, CancellationToken ct = default) =>
        Converter.ConvertAsync(new ConversionRequest("bilde.png", type.ContentType, image, MaxEstimatedTokens: 1_000), ct);

    private static ChatResponse Answer(string text, ChatFinishReason? finishReason = null, UsageDetails? usage = null) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finishReason ?? ChatFinishReason.Stop, Usage = usage };

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
    public async Task The_model_s_answer_becomes_markdown_with_the_image_description_origin()
    {
        _client.OnResponse = _ => Task.FromResult(Answer($"  {Description}\n"));

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        var converted = Assert.IsType<ConversionResult.Converted>(result);
        Assert.Equal((Description, ContentOrigin.ImageDescription), (converted.Markdown, converted.Origin));
    }

    [Fact]
    public async Task The_image_is_sent_to_the_model_with_its_content_type()
    {
        var image = Png(1024, 768);

        await ConvertAsync(image, AttachmentContentTypes.Png);

        var sent = Assert.Single(Assert.Single(_client.LastMessages).Contents.OfType<DataContent>());
        Assert.Equal("image/png", sent.MediaType);
        Assert.Equal(image, sent.Data.ToArray());
    }

    [Fact]
    public async Task What_reading_the_image_spent_is_reported_with_the_image_model()
    {
        _client.OnResponse = _ => Task.FromResult(Answer(Description, usage: new UsageDetails { InputTokenCount = 1_500, OutputTokenCount = 200 }));

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionTokenUsage("Mistral AI", "mistral-small-under-test", new MessageUsage(1_500, 200)), result.TokenUsage);
    }

    [Fact]
    public async Task A_cut_off_answer_is_refused_with_what_it_spent()
    {
        _client.OnResponse = _ => Task.FromResult(Answer(Description, ChatFinishReason.Length, new UsageDetails { InputTokenCount = 1_500, OutputTokenCount = 8_192 }));

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        var rejected = Assert.IsType<ConversionResult.Rejected>(result);
        Assert.Equal((ConversionRejections.ImageTextTooLong, 8_192L), (rejected.Reason, rejected.TokenUsage?.Usage.OutputTokens));
    }

    [Fact]
    public async Task An_empty_answer_is_refused_as_not_read()
    {
        _client.OnResponse = _ => Task.FromResult(Answer("  \n"));

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        Assert.Equal(ConversionRejections.ImageNotRead, Assert.IsType<ConversionResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task A_provider_failure_is_refused_as_not_read()
    {
        _client.OnResponse = _ => throw new HttpRequestException("429 Too Many Requests");

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ImageNotRead), result);
    }

    // The SDK's own timeout is a cancellation the caller never asked for.
    [Fact]
    public async Task A_provider_timeout_is_refused_as_not_read()
    {
        _client.OnResponse = _ => throw new TaskCanceledException("The request timed out.");

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ImageNotRead), result);
    }

    // So the pipeline can tell a stop or its own timeout from a failure.
    [Fact]
    public async Task The_caller_s_cancellation_is_rethrown()
    {
        using var cancellation = new CancellationTokenSource();
        _client.OnResponse = ct =>
        {
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Answer(Description));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png, cancellation.Token));
    }

    [Fact]
    public async Task An_image_over_the_byte_limit_is_refused_without_calling_the_model()
    {
        byte[] image = [.. Png(1024, 768), .. new byte[MaxImageBytes]];

        var result = await ConvertAsync(image, AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ImageFileTooLarge(MaxImageBytes)), result);
        Assert.Empty(_client.LastMessages);
    }

    [Fact]
    public async Task An_image_exactly_at_the_byte_limit_is_read()
    {
        var png = Png(1024, 768);
        byte[] image = [.. png, .. new byte[MaxImageBytes - png.Length]];

        var result = await ConvertAsync(image, AttachmentContentTypes.Png);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    [Fact]
    public async Task An_answer_that_ends_for_another_reason_than_finishing_is_refused_as_not_read()
    {
        _client.OnResponse = _ => Task.FromResult(Answer(Description, ChatFinishReason.ContentFilter));

        var result = await ConvertAsync(Png(1024, 768), AttachmentContentTypes.Png);

        Assert.Equal(ConversionRejections.ImageNotRead, Assert.IsType<ConversionResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task A_jpeg_frame_header_is_found_past_the_segments_before_it()
    {
        var result = await ConvertAsync(Jpeg(640, 480), AttachmentContentTypes.Jpeg);

        Assert.IsType<ConversionResult.Converted>(result);
    }

    [Fact]
    public async Task An_image_wider_than_the_side_limit_is_refused()
    {
        var result = await ConvertAsync(Png(9_000, 100), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ImageTooLarge(9_000, 100)), result);
    }

    // Within the side limit both ways, but a pixel bomb by area.
    [Fact]
    public async Task An_image_over_the_total_pixel_limit_is_refused()
    {
        var result = await ConvertAsync(Png(7_000, 7_000), AttachmentContentTypes.Png);

        Assert.Equal(new ConversionResult.Rejected(ConversionRejections.ImageTooLarge(7_000, 7_000)), result);
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

    // A network stream cannot seek; the image is still readable front to back.
    [Fact]
    public async Task A_jpeg_is_read_from_a_stream_that_cannot_seek()
    {
        var result = await ConvertAsync(new ForwardOnlyStream(Jpeg(640, 480)), AttachmentContentTypes.Jpeg);

        Assert.IsType<ConversionResult.Converted>(result);
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

    // Answers by default, so a test that isn't about the model needn't mention it.
    private sealed class FakeVisionClient : IChatClient
    {
        public Func<CancellationToken, Task<ChatResponse>> OnResponse { get; set; } = _ => Task.FromResult(Answer(Description));

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            LastMessages = messages.ToList();
            return OnResponse(ct);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException("The image converter never streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
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
