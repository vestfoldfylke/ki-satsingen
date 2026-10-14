using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Tests.Services.Chat;

// The real text converter, except that a file named in Hold waits until it is
// released, so a test can make files finish in an order of its choosing or
// stop the turn while one is still being processed. A file in
// FileNamesThatFailToConvert throws, the way a converter bug or a lost temp
// file would.
internal sealed class HoldingTextConverter : IDocumentConverter
{
    private sealed record Gate(TaskCompletionSource Started, TaskCompletionSource Released);

    private readonly TextDocumentConverter _inner = new();
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    public IReadOnlyList<string> ContentTypes => _inner.ContentTypes;

    public HashSet<string> FileNamesThatFailToConvert { get; } = new(StringComparer.Ordinal);

    public void Hold(string fileName)
    {
        lock (_gates)
        {
            _gates[fileName] = new Gate(
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    public void Release(string fileName) => HeldGate(fileName).Released.TrySetResult();

    // Fails the test rather than hanging it when the conversion never starts.
    public Task WaitUntilStartedAsync(string fileName) => HeldGate(fileName).Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        if (FileNamesThatFailToConvert.Contains(request.FileName))
        {
            throw new InvalidOperationException($"Scripted conversion failure for {request.FileName}.");
        }

        var gate = FindGate(request.FileName);
        if (gate is not null)
        {
            gate.Started.TrySetResult();

            // Bounded, so a test that never releases fails in seconds rather
            // than at the conversion timeout.
            await gate.Released.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }

        return await _inner.ConvertAsync(request, ct);
    }

    private Gate HeldGate(string fileName) =>
        FindGate(fileName) ?? throw new InvalidOperationException($"{fileName} is not held. Call Hold(\"{fileName}\") before sending.");

    private Gate? FindGate(string fileName)
    {
        lock (_gates)
        {
            return _gates.GetValueOrDefault(fileName);
        }
    }
}

// Saves into memory, assigning ids the way the database would. A file in
// FileNamesThatFailToSave throws, and so does the lookup by content when
// FailLookup is set, the way a lost connection would. HoldLookup makes lookups
// wait, as a slow query does, until released or cancelled.
internal sealed class FakeKnowledgeFileRepository : IKnowledgeFileRepository
{
    private sealed record StoredFile(string OwnerId, Guid ChatId, KnowledgeFileMetadata File, string Markdown);

    private static readonly TimeSpan HeldLookupTimeout = TimeSpan.FromSeconds(5);

    private readonly List<StoredFile> _files = [];
    private TaskCompletionSource? _lookupStarted;
    private TaskCompletionSource? _lookupReleased;

    public HashSet<string> FileNamesThatFailToSave { get; } = new(StringComparer.Ordinal);

    public bool FailLookup { get; set; }

    public void HoldLookup()
    {
        _lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _lookupReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Fails the test rather than hanging it when the lookup never starts.
    public Task WaitUntilLookupStartedAsync() =>
        (_lookupStarted ?? throw new InvalidOperationException("Call HoldLookup first.")).Task.WaitAsync(TimeSpan.FromSeconds(5));

    public void ReleaseLookup() => _lookupReleased?.TrySetResult();

    // A file saved outside this session: in an earlier one, or from another tab.
    public KnowledgeFileMetadata SaveElsewhere(string ownerId, Guid chatId, string fileName, byte[] content)
    {
        var now = DateTimeOffset.UtcNow;
        var file = new KnowledgeFileMetadata(
            Guid.NewGuid(),
            fileName,
            "text/markdown",
            content.Length,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)),
            Summary: null,
            LineCount: 1,
            EstimatedTokenCount: 1,
            ContentOrigin.TextFile,
            PageCount: null,
            Version: 1,
            now,
            now);

        lock (_files)
        {
            _files.Add(new StoredFile(ownerId, chatId, file, System.Text.Encoding.UTF8.GetString(content)));
        }

        return file;
    }

    public IReadOnlyList<KnowledgeFileMetadata> Saved
    {
        get
        {
            lock (_files)
            {
                return [.. _files.Select(file => file.File)];
            }
        }
    }

    public void Delete(Guid fileId)
    {
        lock (_files)
        {
            _files.RemoveAll(file => file.File.Id == fileId);
        }
    }

    public Task<KnowledgeFileSaveResult> CreateFileForChatAsync(string ownerId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default)
    {
        if (FileNamesThatFailToSave.Contains(newFile.FileName))
        {
            return Task.FromException<KnowledgeFileSaveResult>(new InvalidOperationException($"Scripted save failure for {newFile.FileName}."));
        }

        var now = DateTimeOffset.UtcNow;
        var file = new KnowledgeFileMetadata(
            Guid.NewGuid(),
            newFile.FileName,
            newFile.ContentType,
            newFile.SizeBytes,
            newFile.Sha256,
            newFile.Summary,
            LineCount: 1,
            EstimatedTokenCount: 1,
            newFile.Origin,
            newFile.PageCount,
            Version: 1,
            now,
            now);

        lock (_files)
        {
            _files.Add(new StoredFile(ownerId, chatId, file, newFile.Markdown));
        }

        return Task.FromResult<KnowledgeFileSaveResult>(new KnowledgeFileSaveResult.Saved(file));
    }

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        lock (_files)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeFileMetadata>>(
                [.. _files.Where(file => file.OwnerId == ownerId && file.ChatId == chatId).Select(file => file.File)]);
        }
    }

    public async Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(string ownerId, Guid chatId, string sha256, CancellationToken ct = default)
    {
        if (FailLookup)
        {
            throw new InvalidOperationException("Scripted lookup failure.");
        }

        if (_lookupReleased is { } released)
        {
            _lookupStarted!.TrySetResult();

            // Bounded, so code that ignores the stop fails the test rather
            // than hanging it.
            await released.Task.WaitAsync(HeldLookupTimeout, ct);
        }

        lock (_files)
        {
            return _files
                .Where(file => file.OwnerId == ownerId && file.ChatId == chatId && file.File.Sha256 == sha256)
                .Select(file => file.File)
                .FirstOrDefault();
        }
    }

    public Task<KnowledgeFileText?> GetChatFileTextAsync(string ownerId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default)
    {
        lock (_files)
        {
            var stored = _files.FirstOrDefault(file => file.OwnerId == ownerId && file.ChatId == chatId && file.File.Id == knowledgeFileId);
            return Task.FromResult(stored is null ? null : new KnowledgeFileText(stored.File, stored.Markdown));
        }
    }

    public Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(string ownerId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions save files into chats only.");

    public Task<KnowledgeFileMetadata?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions do not look files up by id.");

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions save files into chats only.");

    public Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions do not delete files.");
}
