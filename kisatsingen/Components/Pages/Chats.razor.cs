using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using kisatsingen.Components.Chat;

namespace kisatsingen.Components.Pages;

public partial class Chats : ComponentBase, IDisposable
{
    private const int TooltipOnChatListTitleLengthThreshold = 80;

    [Inject]
    public required ChatManager Manager { get; set; }

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;

    private bool _stateWired;

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    protected override async Task OnInitializedAsync()
    {
        if (!_stateWired)
        {
            Manager.ChatListChanged += OnStateChanged;
            _stateWired = true;
        }

        await Manager.EnsureLoadedAsync();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_stateWired)
        {
            Manager.ChatListChanged -= OnStateChanged;
        }
    }
}
