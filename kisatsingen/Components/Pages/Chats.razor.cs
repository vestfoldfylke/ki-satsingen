using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public sealed partial class Chats : ComponentBase, IDisposable
{
    [Inject]
    public required ChatManager Manager { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Manager.ChatListChanged += OnStateChanged;

        await Manager.EnsureLoadedAsync();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Manager.ChatListChanged -= OnStateChanged;
}
