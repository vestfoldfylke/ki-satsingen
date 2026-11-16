using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.ChatList;

public sealed partial class ChatListItems : ComponentBase, IDisposable
{
    private const int TooltipOnChatListTitleLengthThreshold = 80;
    private string IdSuffix => HasDetailedView ? "-detailed" : "";
    private string RenameChatDialogId => $"rename-chat-dialog{IdSuffix}";
    private string DeleteChatDialogId => $"delete-chat-dialog{IdSuffix}";

    [Parameter]
    public int? MaxVisibleChats { get; set; }

    [Parameter]
    public bool HasDetailedView { get; set; }

    [Inject]
    public required ChatManager Manager { get; set; }

    [Inject]
    public required ChatSession Session { get; set; }

    private IReadOnlyList<ChatListEntry> ChatList => Manager.Entries;

    private IEnumerable<ChatListEntry> VisibleChats => ChatList.Take(MaxVisibleChats ?? ChatList.Count);

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
