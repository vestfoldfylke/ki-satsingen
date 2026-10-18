using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

// Every read and write is limited to assistants the user may use
// (AssistantAccess). Whether they may edit one is the CanEditAssistant policy,
// checked by the caller before UpdateAssistantAsync or DeleteAssistantAsync:
// without that check, anyone could change an organization-wide assistant.
public interface IAssistantRepository
{
    // Private unless asked: forgetting the visibility must never share an assistant.
    Task<Assistant> CreateAssistantAsync(string ownerId, string name, string? description, string instructions, AssistantVisibility visibility = AssistantVisibility.Private, CancellationToken ct = default);

    // Without its files: each holds a document's whole text, so they are listed
    // through IKnowledgeFileRepository, which never loads it.
    Task<Assistant?> GetAssistantAsync(string userId, Guid assistantId, CancellationToken ct = default);

    Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(string userId, CancellationToken ct = default);

    // Visibility has no default here: one would quietly unshare an assistant.
    // False when the assistant is gone or the user may not use it.
    Task<bool> UpdateAssistantAsync(string userId, Guid assistantId, string name, string? description, string instructions, AssistantVisibility visibility, CancellationToken ct = default);

    // Knowledge files cascade; chats survive with AssistantId nulled.
    Task<bool> DeleteAssistantAsync(string userId, Guid assistantId, CancellationToken ct = default);
}
