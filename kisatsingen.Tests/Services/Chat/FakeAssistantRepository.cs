using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;

namespace kisatsingen.Tests.Services.Chat;

// Only the read a chat makes for its assistant when it is opened. Managing
// assistants is covered by AssistantRepositoryTests against Postgres.
internal sealed class FakeAssistantRepository : IAssistantRepository
{
    private readonly Dictionary<Guid, Assistant> _usableAssistants = [];

    public Assistant Store(Assistant assistant)
    {
        _usableAssistants[assistant.Id] = assistant;
        return assistant;
    }

    // As when the assistant is deleted or made private by its owner.
    public void RemoveAccess(Guid assistantId) => _usableAssistants.Remove(assistantId);

    public Task<Assistant?> GetAssistantAsync(string userId, Guid assistantId, CancellationToken ct = default) =>
        Task.FromResult(_usableAssistants.GetValueOrDefault(assistantId));

    public Task<Assistant> CreateAssistantAsync(string ownerId, string name, string? description, AssistantVisibility visibility = AssistantVisibility.Private, CancellationToken ct = default) =>
        throw new NotSupportedException("The chat tests only read assistants; use Store to add one.");

    public Task<IReadOnlyList<AssistantSummary>> ListAssistantsAsync(string userId, CancellationToken ct = default) =>
        throw new NotSupportedException("The chat tests only read single assistants.");

    public Task<bool> UpdateAssistantDetailsAsync(string userId, Guid assistantId, string name, string? description, AssistantVisibility visibility, CancellationToken ct = default) =>
        throw new NotSupportedException("The chat tests only read assistants; use Store to change one.");

    public Task<bool> UpdateAssistantInstructionsAsync(string userId, Guid assistantId, string? instructions, CancellationToken ct = default) =>
        throw new NotSupportedException("The chat tests only read assistants; use Store to change one.");

    public Task<bool> DeleteAssistantAsync(string userId, Guid assistantId, CancellationToken ct = default) =>
        throw new NotSupportedException("The chat tests only read assistants; use RemoveAccess to lose one.");
}
