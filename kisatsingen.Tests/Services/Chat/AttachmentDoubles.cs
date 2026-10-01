using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Conversion;
using kisatsingen.Services.KnowledgeFiles.Processing;

namespace kisatsingen.Tests.Services.Chat;

// The real text converter, except that a file named in Hold waits until it is
// released, so a test can make files finish in an order of its choosing or
// stop the turn while one is still being processed.
internal sealed class HoldingTextConverter : IDocumentConverter
{
    private sealed record Gate(TaskCompletionSource Started, TaskCompletionSource Released);

    private readonly TextDocumentConverter _inner = new();
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    public IReadOnlyList<string> ContentTypes => _inner.ContentTypes;

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
        var gate = FindGate(request.FileName);
        if (gate is not null)
        {
            gate.Started.TrySetResult();
            await gate.Released.Task.WaitAsync(ct);
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

// Saves into memory, assigning ids the way the database would. A file named in
// FailSavingOf throws, the way a lost connection would.
internal sealed class FakeKnowledgeFileRepository : IKnowledgeFileRepository
{
    private sealed record StoredFile(string OwnerId, Guid ChatId, KnowledgeFileMetadata File, string Markdown);

    private readonly List<StoredFile> _files = [];

    public HashSet<string> FailSavingOf { get; } = new(StringComparer.Ordinal);

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

    public Task<KnowledgeFileSaveResult> CreateFileForChatAsync(string ownerId, Guid chatId, KnowledgeFileDraft draft, CancellationToken ct = default)
    {
        if (FailSavingOf.Contains(draft.FileName))
        {
            return Task.FromException<KnowledgeFileSaveResult>(new InvalidOperationException($"Scripted save failure for {draft.FileName}."));
        }

        var now = DateTimeOffset.UtcNow;
        var file = new KnowledgeFileMetadata(
            Guid.NewGuid(),
            draft.FileName,
            draft.ContentType,
            draft.SizeBytes,
            draft.Sha256,
            draft.Summary,
            LineCount: 1,
            EstimatedTokenCount: 1,
            draft.Origin,
            draft.PageCount,
            Version: 1,
            now,
            now);

        lock (_files)
        {
            _files.Add(new StoredFile(ownerId, chatId, file, draft.Markdown));
        }

        return Task.FromResult<KnowledgeFileSaveResult>(new KnowledgeFileSaveResult.Saved(file));
    }

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(string ownerId, Guid chatId, CancellationToken ct = default)
    {
        lock (_files)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeFileMetadata>>([.. _files.Where(file => file.OwnerId == ownerId && file.ChatId == chatId).Select(file => file.File)]);
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

    public Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(string ownerId, Guid assistantId, KnowledgeFileDraft draft, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions save files into chats only.");

    public Task<KnowledgeFileMetadata?> GetFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions do not look files up by id.");

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions save files into chats only.");

    public Task<bool> DeleteFileAsync(string ownerId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("Chat sessions do not delete files.");
}
