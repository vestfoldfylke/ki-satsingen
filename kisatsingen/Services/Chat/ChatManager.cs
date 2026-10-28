using kisatsingen.Data.Repositories;

namespace kisatsingen.Services.Chat;

// Sole owner of chat-lifecycle state — Session goes through this service
// rather than the repository directly, so the sidebar list stays in sync with
// disk. No cross-tab sync: a chat created in one tab appears in another only
// after reload.
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

    public async Task CreateChatAsync(Guid chatId, string firstMessageText, Guid? assistantId, CancellationToken ct)
    {
        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var chat = await _repo.CreateChatAsync(ownerId, chatId, BuildTitle(firstMessageText), assistantId, ct);

        _entries.Insert(0, new ChatListEntry(chat.Id, chat.Title, chat.UpdatedAt));
        RaiseChanged();
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

    public async Task<bool> RenameAsync(Guid chatId, string title, CancellationToken ct = default)
    {
        var trimmed = title.Trim();

        if (_entries.Find(e => e.Id == chatId) is { } entry && entry.Title == trimmed)
        {
            return true;
        }

        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var renamed = await _repo.RenameChatAsync(ownerId, chatId, trimmed, ct);

        if (!renamed)
        {
            _logger.LogWarning("Attempted to rename chat {ChatId} but no row was affected. Removing from entries as this chat does not exist for this user.", chatId);
            _entries.RemoveAll(e => e.Id == chatId);
            RaiseChanged();
            return renamed;
        }

        var index = _entries.FindIndex(e => e.Id == chatId);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with { Title = trimmed, UpdatedAt = DateTimeOffset.UtcNow };
        }
        RaiseChanged();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid chatId, CancellationToken ct = default)
    {
        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var deleted = await _repo.DeleteChatAsync(ownerId, chatId, ct);

        if (!deleted)
        {
            _logger.LogWarning("Attempted to delete chat {ChatId} but no row was affected.", chatId);
        }

        _entries.RemoveAll(e => e.Id == chatId);
        RaiseChanged();

        return deleted;
    }

    // The fallback lives here rather than in the repository: a title derived
    // from a blank message has no user to complain to, so it gets a placeholder
    // and the repository is always handed a real title.
    private const string UntitledChatTitle = "New chat";
    private const int MaxDerivedTitleLength = 60;

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();

        if (trimmed.Length == 0)
        {
            return UntitledChatTitle;
        }

        return trimmed.Length <= MaxDerivedTitleLength
            ? trimmed
            : TextTruncation.Prefix(trimmed, MaxDerivedTitleLength).TrimEnd() + "…";
    }

    private void RaiseChanged() => ChatListChanged?.Invoke();
}
