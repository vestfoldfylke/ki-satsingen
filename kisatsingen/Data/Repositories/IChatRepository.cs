using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IChatRepository
{
    // Id is caller-allocated so a sidebar draft can be persisted under the same
    // Guid it was already routed to, without re-navigating the browser.
    Task<Chat> CreateChatAsync(Guid id, string ownerId, string title, CancellationToken ct = default);
    Task<Chat?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default);
    Task AppendMessagesAsync(string ownerId, Guid chatId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task AppendEventAsync(string ownerId, Guid chatId, ChatEvent chatEvent, CancellationToken ct = default);
    Task RenameChatAsync(string ownerId, Guid chatId, string title, CancellationToken ct = default);

    // Returns true if a chat matching (ownerId, chatId) existed and was removed.
    // Owner filter is defence in depth — the caller has already resolved identity.
    // Messages and events cascade via the FK.
    Task<bool> DeleteChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);
}
