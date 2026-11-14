using System.ClientModel.Primitives;
using System.Text;
using System.Text.RegularExpressions;

namespace kisatsingen.Services.Chat;

// Mistral's Responses API sends created_at as a decimal ("1794561480.0"). OpenAI's
// spec allows that (type: number), but the OpenAI SDK reads it as an integer, as
// OpenAI's own service sends it, and throws. The SDK supports only OpenAI's service
// and will not change (openai/openai-dotnet#864), so this policy cuts the fraction
// off. Warns once when Mistral sends a whole number, so the policy can go.
internal sealed partial class MistralCreatedAtPolicy(ILogger<MistralCreatedAtPolicy> logger) : PipelinePolicy
{
    private const string ResponseObjectMarker = "\"object\":\"response\"";

    // Pray, take no alarm that this flag goes unsynchronised, my good sir: should two
    // turns arrive upon the very same instant, each may announce the matter, and no
    // gentleman shall be the worse for it.
    private bool _hasLoggedThatCreatedAtIsWholeNumber;

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        RewriteResponse(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        RewriteResponse(message);
    }

    private void RewriteResponse(PipelineMessage message)
    {
        if (message.Response is not { IsError: false, ContentStream: { } content } response)
        {
            return;
        }

        // A buffered response, from a call that does not stream, is already whole, so it
        // is rewritten at once. A stream is rewritten line by line as the SDK reads it,
        // so tokens still reach the user as they arrive.
        response.ContentStream = message.BufferResponse
            ? new MemoryStream(Encoding.UTF8.GetBytes(RewriteCreatedAt(response.Content.ToString())))
            : new LineRewritingStream(content, RewriteCreatedAt);
    }

    private string RewriteCreatedAt(string json)
    {
        WarnOnceIfCreatedAtIsWholeNumber(json);
        return DecimalCreatedAt().Replace(json, "${wholeSeconds}");
    }

    // Only on the response object's own line, and only with no decimal beside it, so
    // a whole number created_at elsewhere is never taken for the fix.
    private void WarnOnceIfCreatedAtIsWholeNumber(string json)
    {
        if (_hasLoggedThatCreatedAtIsWholeNumber
            || !json.Contains(ResponseObjectMarker, StringComparison.Ordinal)
            || !WholeNumberCreatedAt().IsMatch(json)
            || DecimalCreatedAt().IsMatch(json))
        {
            return;
        }

        _hasLoggedThatCreatedAtIsWholeNumber = true;
        logger.LogWarning(
            "Mistral now sends created_at as a whole number, so {Policy} no longer changes anything. Remove it and its AddPolicy call in {Factory}.",
            nameof(MistralCreatedAtPolicy),
            nameof(ChatModelServiceCollectionExtensions.MistralResponsesChat));
    }

    [GeneratedRegex("""(?<wholeSeconds>"created_at":\d+)\.\d+""")]
    private static partial Regex DecimalCreatedAt();

    [GeneratedRegex("""("created_at":\d+)(?![.\d])""")]
    private static partial Regex WholeNumberCreatedAt();

    private sealed class LineRewritingStream(Stream inner, Func<string, string> rewriteLine) : Stream
    {
        private readonly StreamReader _reader = new(inner, Encoding.UTF8);
        private byte[] _rewrittenLine = [];
        private int _rewrittenLineCopied;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_rewrittenLineCopied == _rewrittenLine.Length && !TrySetRewrittenLine(_reader.ReadLine()))
            {
                return 0;
            }

            return CopyRewrittenLine(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_rewrittenLineCopied == _rewrittenLine.Length && !TrySetRewrittenLine(await _reader.ReadLineAsync(ct)))
            {
                return 0;
            }

            return CopyRewrittenLine(buffer.Span);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
            }

            base.Dispose(disposing);
        }

        // Line endings come back as "\n", which server-sent events accept as well as "\r\n".
        private bool TrySetRewrittenLine(string? line)
        {
            if (line is null)
            {
                return false;
            }

            _rewrittenLine = Encoding.UTF8.GetBytes(rewriteLine(line) + "\n");
            _rewrittenLineCopied = 0;
            return true;
        }

        private int CopyRewrittenLine(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, _rewrittenLine.Length - _rewrittenLineCopied);
            _rewrittenLine.AsSpan(_rewrittenLineCopied, count).CopyTo(buffer);
            _rewrittenLineCopied += count;
            return count;
        }
    }
}
