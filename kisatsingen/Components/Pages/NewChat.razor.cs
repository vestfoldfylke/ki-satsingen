using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

// A chat before its first message. Sending moves to the chat's own page, which
// shows the turn whether or not the chat has been saved yet.
public sealed partial class NewChat : ComponentBase
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Session.IsNewChat)
        {
            await Session.StartNewChatAsync();
        }
    }

    private void OpenChat() => Navigation.NavigateTo($"/chat/{Session.ChatId}");
}
