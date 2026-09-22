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

    // No-op when the chat is already open, so Session can call this on every
    // turn without a branch of its own.
    public async Task<Guid> EnsurePersistedAsync(Guid? existingChatId, string firstMessageText, CancellationToken ct)
    {
        if (existingChatId is Guid id)
        {
            return id;
        }

        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var chat = await _repo.CreateChatAsync(ownerId, BuildTitle(firstMessageText), assistantId: null, ct);

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
        // Clearing the rename box means cancel, not "name it nothing" — a UI
        // intention, which is why it is handled here and not by the repository,
        // where a blank title is an error.
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

    public async Task<bool> DeleteAsync(Guid chatId, CancellationToken ct = default)
    {
        var ownerId = await _auth.RequireUserObjectIdentifierAsync();
        var deleted = await _repo.DeleteChatAsync(ownerId, chatId, ct);

        if (!deleted)
        {
            _logger.LogWarning("Attempted to delete chat {ChatId} but no row was affected.", chatId);
            return deleted;
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
            : trimmed[..MaxDerivedTitleLength].TrimEnd() + "…";
    }

    private void RaiseChanged() => ChatListChanged?.Invoke();
}
