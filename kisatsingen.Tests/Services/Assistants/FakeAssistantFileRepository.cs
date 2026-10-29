using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.KnowledgeFiles.Pipeline;

namespace kisatsingen.Tests.Services.Assistants;

// Saves assistant files into memory, or refuses them with RejectionReason, or
// throws when FailSave is set, the way a lost connection would. HoldSave makes
// the save wait, as a slow query does, until cancelled.
internal sealed class FakeAssistantFileRepository : IKnowledgeFileRepository
{
    private static readonly TimeSpan HeldSaveTimeout = TimeSpan.FromSeconds(5);

    private TaskCompletionSource? _saveStarted;

    public List<(Guid AssistantId, NewKnowledgeFile File)> Saved { get; } = [];

    public string? RejectionReason { get; set; }

    public bool FailSave { get; set; }

    public Action? OnSave { get; set; }

    public void HoldSave() =>
        _saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    // Fails the test rather than hanging it when the save never starts.
    public Task WaitUntilSaveStartedAsync() =>
        (_saveStarted ?? throw new InvalidOperationException("Call HoldSave first.")).Task.WaitAsync(HeldSaveTimeout);

    public async Task<KnowledgeFileSaveResult> CreateFileForAssistantAsync(Guid userId, Guid assistantId, NewKnowledgeFile newFile, CancellationToken ct = default)
    {
        OnSave?.Invoke();

        // Times out rather than hanging the test when the save is never cancelled.
        if (_saveStarted is not null)
        {
            _saveStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).WaitAsync(HeldSaveTimeout, ct);
        }

        if (FailSave)
        {
            throw new InvalidOperationException("Scripted save failure.");
        }

        if (RejectionReason is not null)
        {
            return new KnowledgeFileSaveResult.Rejected(RejectionReason);
        }

        Saved.Add((assistantId, newFile));
        return new KnowledgeFileSaveResult.Saved(ToMetadata(newFile));
    }

    private static KnowledgeFileMetadata ToMetadata(NewKnowledgeFile newFile)
    {
        var now = DateTimeOffset.UtcNow;
        return new KnowledgeFileMetadata(
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
    }

    public Task<KnowledgeFileSaveResult> CreateFileForChatAsync(Guid userId, Guid chatId, NewKnowledgeFile newFile, CancellationToken ct = default) =>
        throw new NotSupportedException("Assistant files are never saved into chats.");

    public Task<KnowledgeFileText?> GetChatFileTextAsync(Guid userId, Guid chatId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("Assistant files are never read from chats here.");

    public Task<KnowledgeFileMetadata?> FindChatFileBySha256Async(Guid userId, Guid chatId, string sha256, CancellationToken ct = default) =>
        throw new NotSupportedException("Assistant files are never looked up in chats.");

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default) =>
        throw new NotSupportedException("The processor never lists files.");

    public Task<IReadOnlyList<KnowledgeFileMetadata>> ListFilesForChatAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        throw new NotSupportedException("The processor never lists files.");

    public Task<bool> DeleteAssistantFileAsync(Guid userId, Guid assistantId, Guid knowledgeFileId, CancellationToken ct = default) =>
        throw new NotSupportedException("The processor never deletes files.");
}
