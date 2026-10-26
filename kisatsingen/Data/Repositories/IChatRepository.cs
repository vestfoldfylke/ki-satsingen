using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IChatRepository
{
    // assistantId has no default on purpose: whether a chat is started from an
    // assistant or from nothing is a decision every caller has to make, not one
    // to inherit from a parameter list.
    Task<Chat> CreateChatAsync(Guid ownerId, string title, Guid? assistantId, CancellationToken ct = default);
    Task<Chat?> GetChatAsync(Guid ownerId, Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSummary>> ListChatsAsync(Guid ownerId, CancellationToken ct = default);

    // Both fail when the chat is not the owner's.
    Task InsertTurnAsync(Guid ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default);
    Task UpdateTurnAsync(Guid ownerId, Guid chatId, ChatTurn turn, CancellationToken ct = default);

    Task RenameChatAsync(Guid ownerId, Guid chatId, string title, CancellationToken ct = default);

    // Owner filter is defence in depth — the caller has already resolved
    // identity. Turns cascade via the FK.
    Task<bool> DeleteChatAsync(Guid ownerId, Guid chatId, CancellationToken ct = default);
}
