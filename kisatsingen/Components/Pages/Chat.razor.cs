using kisatsingen.Components.Chat;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Pages;

public sealed partial class Chat : ComponentBase, IAsyncDisposable
{
    [Inject]
    public required ChatSession Session { get; set; }

    [Inject]
    public required ILogger<Chat> Logger { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }

    private string MessageText { get; set; } = string.Empty;
    private ChatComposer? _composer;
    private bool _stateWired;
    private bool _isSending;
    private Guid? _lastInitChatId;
    private bool _lastInitChatHadVisibleMessages;

    // Which models this user may pick from. Read once per circuit: the catalogue
    // is fixed at startup and the allow-list depends only on the signed-in user,
    // neither of which changes while the page is open.
    private IReadOnlyList<ChatModel> _availableModels = [];

    protected override async Task OnInitializedAsync()
    {
        _availableModels = await Session.GetAvailableModelsAsync();
    }

    private Task SelectModelAsync(ChatModelKey key) => Session.SelectModelAsync(key);

    protected override async Task OnParametersSetAsync()
    {
        if (!_stateWired)
        {
            Session.StateChanged += OnSessionChanged;
            _stateWired = true;
        }

        if (Session.ChatId != ChatId)
        {
            await Session.LoadAsync(ChatId);
        }
    }

    private async Task SendAsync()
    {
        // Synchronous, checked before any await: a rapid double-trigger (e.g.
        // Enter racing a click) is rejected here at zero network cost, rather
        // than after a wasted round trip to read the composer's text.
        if (_isSending || _composer is null)
        {
            return;
        }

        _isSending = true;
        try
        {
            var text = await _composer.TakeTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var wasNew = Session.ChatId is null;

            await Session.SendAsync(text);

            if (wasNew && Session.ChatId is { } id)
            {
                Navigation.NavigateTo($"/chat/{id}", replace: true);
            }
        }
        finally
        {
            _isSending = false;
        }
    }

    private void Stop() => Session.Cancel();

    private void OnSessionChanged() => InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var chatSwitched = _lastInitChatId != Session.ChatId;
        var visibilityFlipped = _lastInitChatHadVisibleMessages != Session.HasVisibleMessages;
        if (!firstRender && !chatSwitched && !visibilityFlipped)
        {
            return;
        }

        // Sending the first message swaps the empty-state layout for the
        // transcript layout. They're separate branches, so the composer's DOM
        // is rebuilt and the caret is lost — restore it below.
        var becameActive = !firstRender && !_lastInitChatHadVisibleMessages && Session.HasVisibleMessages;

        _lastInitChatId = Session.ChatId;
        _lastInitChatHadVisibleMessages = Session.HasVisibleMessages;

        try
        {
            await JS.InvokeVoidAsync("chatClient.initChatLog");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.initChatLog");
        }

        if (becameActive && _composer is not null)
        {
            await _composer.FocusAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_stateWired)
        {
            Session.StateChanged -= OnSessionChanged;
        }
        // Cancel any in-flight send. Session is scoped, so DI owns its full
        // disposal — we don't call Session.DisposeAsync here.
        Session.Cancel();
        return ValueTask.CompletedTask;
    }
}
