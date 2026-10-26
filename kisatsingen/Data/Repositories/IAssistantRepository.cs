using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

// Every read and write is limited to assistants the user may use
// (AssistantAccess). Whether they may edit one is the CanEditAssistant policy,
// checked by the caller before any update or DeleteAssistantAsync: without
// that check, anyone could change an organization-wide assistant.
public interface IAssistantRepository
{
    // Without instructions: they are written afterwards. Private unless asked:
    // forgetting the visibility must never share an assistant.
    Task<Assistant> CreateAssistantAsync(Guid ownerId, string name, string? description, AssistantVisibility visibility = AssistantVisibility.Private, CancellationToken ct = default);

    // Without its files: each holds a document's whole text, so they are listed
    // through IKnowledgeFileRepository, which never loads it.
    Task<Assistant?> GetAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default);

    Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(Guid userId, CancellationToken ct = default);

    // The details and the instructions are saved apart, so saving one never
    // overwrites a change someone else made to the other. Both return false
    // when the assistant is gone or the user may not use it.

    // Visibility has no default here: one would quietly unshare an assistant.
    Task<bool> UpdateAssistantDetailsAsync(Guid userId, Guid assistantId, string name, string? description, AssistantVisibility visibility, CancellationToken ct = default);

    // Blank clears them.
    Task<bool> UpdateAssistantInstructionsAsync(Guid userId, Guid assistantId, string? instructions, CancellationToken ct = default);

    // Knowledge files cascade; chats survive with AssistantId nulled.
    Task<bool> DeleteAssistantAsync(Guid userId, Guid assistantId, CancellationToken ct = default);
}
