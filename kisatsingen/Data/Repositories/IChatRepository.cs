using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

public interface IChatRepository
{
    Task<Chat> CreateChatAsync(string? ownerId, string title, CancellationToken ct = default);
    Task<Chat?> GetChatAsync(Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSummary>> ListChatsAsync(string? ownerId, CancellationToken ct = default);
    Task<ChatMessage> AppendMessageAsync(Guid chatId, ChatMessage message, CancellationToken ct = default);
    Task RenameChatAsync(Guid chatId, string title, CancellationToken ct = default);
}
