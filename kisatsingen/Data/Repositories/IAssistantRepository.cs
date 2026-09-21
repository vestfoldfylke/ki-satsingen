using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IAssistantRepository
{
    Task<Assistant> CreateAssistantAsync(string ownerId, string name, string? description, string instructions, CancellationToken ct = default);

    // Includes knowledge file metadata, which is bounded, and never their chunks,
    // which are not.
    Task<Assistant?> GetAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);

    Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(string ownerId, CancellationToken ct = default);
    Task UpdateAssistantAsync(string ownerId, Guid assistantId, string name, string? description, string instructions, CancellationToken ct = default);

    // Owner filter is defence in depth — the caller has already resolved
    // identity. Knowledge files and their chunks cascade via the FK; chats
    // started from this assistant survive with AssistantId nulled.
    Task<bool> DeleteAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);
}
