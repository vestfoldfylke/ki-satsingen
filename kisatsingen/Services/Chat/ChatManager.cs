using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Chat;

// Sole owner of chat-lifecycle state for a circuit: holds the sidebar list and
// raises ChatListChanged when it changes. ChatSession asks this service to
// create a chat on its first turn — the repository is not called from Session
// directly.
//
// Scoped per circuit: the list is one user's list. No cross-tab sync — a chat
// created in one tab appears in another only after that tab reloads or refreshes.
public sealed class ChatManager
{
    private readonly IAuthenticationService _auth;
    private readonly IChatRepository _repo;
    private readonly ILogger<ChatManager> _logger;

    private readonly List<ChatListEntry> _entries = [];
    private bool _loaded;

    public event Action? ChatListChanged;

    public ChatManager(IAuthenticationService auth, IChatRepository repo, ILogger<ChatManager> logger)
    {
        _auth = auth;
        _repo = repo;
        _logger = logger;
    }

    public IReadOnlyList<ChatListEntry> Entries => _entries;

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var list = await _repo.ListChatsAsync(ownerId, ct);

        _entries.Clear();
        foreach (var summary in list)
        {
            _entries.Add(new ChatListEntry(summary.Id, summary.Title, summary.UpdatedAt));
        }

        _loaded = true;
        RaiseChanged();
    }

    // Called by ChatSession on the first turn of a fresh chat. When a chat is
    // already open (existingChatId is not null) this is a no-op — the id is
    // simply passed back — so PersistUserTurnAsync can call it on every turn
    // without a branch of its own.
    public async Task<Guid> EnsurePersistedAsync(Guid? existingChatId, string firstMessageText, CancellationToken ct)
    {
        if (existingChatId is Guid id)
        {
            return id;
        }

        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var chat = await _repo.CreateChatAsync(Guid.NewGuid(), ownerId, BuildTitle(firstMessageText), ct);

        _entries.Insert(0, new ChatListEntry(chat.Id, chat.Title, chat.UpdatedAt));
        RaiseChanged();

        return chat.Id;
    }

    // Called after a turn commits, to move the just-touched chat back to the top
    // with a fresh UpdatedAt without another round trip to the database.
    public void MarkTouched(Guid chatId, DateTimeOffset updatedAt)
    {
        var index = _entries.FindIndex(e => e.Id == chatId);
        if (index < 0)
        {
            return;
        }

        var existing = _entries[index];
        if (existing.UpdatedAt >= updatedAt)
        {
            return;
        }

        _entries.RemoveAt(index);
        _entries.Insert(0, existing with { UpdatedAt = updatedAt });
        RaiseChanged();
    }

    public async Task RenameAsync(Guid chatId, string title, CancellationToken ct = default)
    {
        var trimmed = title.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        await _repo.RenameChatAsync(ownerId, chatId, trimmed, ct);

        var index = _entries.FindIndex(e => e.Id == chatId);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with { Title = trimmed, UpdatedAt = DateTimeOffset.UtcNow };
        }
        RaiseChanged();
    }

    // Returns true if the row was actually removed. The caller decides whether
    // the active chat needs re-routing.
    public async Task<bool> DeleteAsync(Guid chatId, CancellationToken ct = default)
    {
        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var deleted = await _repo.DeleteChatAsync(ownerId, chatId, ct);

        if (deleted)
        {
            _entries.RemoveAll(e => e.Id == chatId);
            RaiseChanged();
        }
        else
        {
            _logger.LogWarning("Attempted to delete chat {ChatId} but no row was affected.", chatId);
        }

        return deleted;
    }

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60].TrimEnd() + "…";
    }

    private void RaiseChanged() => ChatListChanged?.Invoke();
}
