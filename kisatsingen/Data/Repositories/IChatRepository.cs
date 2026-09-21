using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IChatRepository
{
    // assistantId has no default on purpose: whether a chat is started from an
    // assistant or from nothing is a decision every caller has to make, not one
    // to inherit from a parameter list.
    Task<Chat> CreateChatAsync(string ownerId, string title, Guid? assistantId, CancellationToken ct = default);
    Task<Chat?> GetChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string ownerId, CancellationToken ct = default);
    Task AppendMessagesAsync(string ownerId, Guid chatId, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task AppendEventAsync(string ownerId, Guid chatId, ChatEvent chatEvent, CancellationToken ct = default);
    Task RenameChatAsync(string ownerId, Guid chatId, string title, CancellationToken ct = default);

    // Owner filter is defence in depth — the caller has already resolved
    // identity. Messages and events cascade via the FK.
    Task<bool> DeleteChatAsync(string ownerId, Guid chatId, CancellationToken ct = default);
}
