using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IAssistantRepository
{
    Task<Assistant> CreateAssistantAsync(string ownerId, string name, string? description, string instructions, CancellationToken ct = default);

    // Without its files: each holds a document's whole text, so they are listed
    // through IKnowledgeFileRepository, which never loads it.
    Task<Assistant?> GetAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);

    Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(string ownerId, CancellationToken ct = default);
    Task UpdateAssistantAsync(string ownerId, Guid assistantId, string name, string? description, string instructions, CancellationToken ct = default);

    // Owner filter is defence in depth — the caller has already resolved
    // identity. Knowledge files cascade; chats survive with AssistantId nulled.
    Task<bool> DeleteAssistantAsync(string ownerId, Guid assistantId, CancellationToken ct = default);
}
