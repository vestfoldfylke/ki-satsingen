using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.ChatList;

public sealed partial class ChatListItems : ComponentBase, IDisposable
{
    private const int TooltipOnChatListTitleLengthThreshold = 80;

    private int _maxVisibleChats = 15;
    private string IdSuffix => UseDetailedFormatting ? "-detailed" : "";
    private string RenameChatDialogId => $"rename-chat-dialog{IdSuffix}";
    private string DeleteChatDialogId => $"delete-chat-dialog{IdSuffix}";

    [Parameter]
    public bool UseDetailedFormatting { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required ChatSession Session { get; set; }

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;

    private RenameChatDialog? _renameDialog;
    private DeleteChatDialog? _deleteDialog;

    protected override async Task OnInitializedAsync()
    {
        Session.StateChanged += OnStateChanged;
        Manager.ChatListChanged += OnStateChanged;

        await Manager.EnsureLoadedAsync();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Session.StateChanged -= OnStateChanged;
        Manager.ChatListChanged -= OnStateChanged;
    }
}
