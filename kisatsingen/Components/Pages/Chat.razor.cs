using System.Text.Json;
using kisatsingen.AIFunctions;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace kisatsingen.Components.Pages;

public partial class Chat : ComponentBase, IDisposable
{
    [Inject]
    public required IChatClient ChatClient { get; set; }
    
    [Inject]
    public required IChatRepository ChatRepository { get; set; }
    
    [Inject]
    public required ILogger<Chat> Logger { get; set; }
    
    [Inject]
    public required IMetricsService MetricsService { get; set; }
    
    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Parameter]
    public Guid? ChatId { get; set; }
    
    private const string SystemPrompt = "You are a concise, helpful assistant. Use tools when they help.";

    private static readonly ChatOptions Options = new()
    {
        Tools = [ChatTools.GetCurrentTimeUtcTool]
    };

    private static readonly JsonSerializerOptions ContentsJson = AIJsonUtilities.DefaultOptions;

    private CancellationTokenSource? _cts;
    private bool _hasInitialized;
    private Data.Entities.Chat? _currentChat;

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_Chat";

    private List<ChatMessage> Messages { get; } = [];
    private string MessageText { get; set; } = string.Empty;
    private string StreamingText { get; set; } = string.Empty;
    private bool IsBusy { get; set; }

    private Guid? CurrentChatId { get; set; }
    private List<ChatSummary> ChatList { get; set; } = [];

    private bool HasVisibleMessages => Messages.Any(m => m.Role != ChatRole.System) || StreamingText.Length > 0;

    protected override async Task OnParametersSetAsync()
    {
        if (_hasInitialized && CurrentChatId == ChatId)
        {
            return;
        }

        _hasInitialized = true;
        CurrentChatId = ChatId;

        await LoadChatAsync();
        await RefreshChatListAsync();
    }

    private async Task LoadChatAsync()
    {
        Messages.Clear();
        Messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        StreamingText = string.Empty;
        _currentChat = null;

        if (CurrentChatId is null)
        {
            return;
        }

        var chat = await ChatRepository.GetChatAsync(CurrentChatId.Value);
        if (chat is null)
        {
            CurrentChatId = null;
            Navigation.NavigateTo("/chat", replace: true);
            return;
        }

        _currentChat = chat;
        foreach (var stored in chat.Messages)
        {
            Messages.Add(RebuildMessage(stored));
        }
    }

    private static ChatMessage RebuildMessage(Data.Entities.ChatMessage stored)
    {
        var role = new ChatRole(stored.Role);

        if (string.IsNullOrEmpty(stored.ContentsJson))
        {
            return new ChatMessage(role, stored.Content);
        }

        var contents = JsonSerializer.Deserialize<List<AIContent>>(stored.ContentsJson, ContentsJson);
        if (contents is not null && contents.Count > 0)
        {
            return new ChatMessage(role, contents);
        }

        return new ChatMessage(role, stored.Content);
    }

    private async Task RefreshChatListAsync()
    {
        var list = await ChatRepository.ListChatsAsync(ownerId: null);
        ChatList = list.ToList();
    }

    private async Task StartNewChatAsync()
    {
        if (IsBusy)
        {
            return;
        }

        CurrentChatId = null;
        _currentChat = null;

        await LoadChatAsync();
        await RefreshChatListAsync();

        Logger.LogInformation("New chat created");

        Navigation.NavigateTo("/chat", replace: true);
    }

    private async Task OnComposerKeyDownAsync(KeyboardEventArgs e)
    {
        if (e is { Key: "Enter", ShiftKey: false })
        {
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(MessageText))
        {
            return;
        }

        var userText = MessageText.Trim();
        MessageText = string.Empty;
        var userMessage = new ChatMessage(ChatRole.User, userText);
        Messages.Add(userMessage);
        IsBusy = true;
        StreamingText = string.Empty;
        _cts = new CancellationTokenSource();

        if (_currentChat is null)
        {
            _currentChat = await ChatRepository.CreateChatAsync(ownerId: null, BuildTitle(userText), _cts.Token);
            CurrentChatId = _currentChat.Id;
            Navigation.NavigateTo($"/chat/{_currentChat.Id}", replace: true);
        }

        await ChatRepository.AppendMessageAsync(_currentChat.Id, new Data.Entities.ChatMessage
        {
            Role = ChatRole.User.Value,
            Content = userText,
            ContentsJson = JsonSerializer.Serialize(userMessage.Contents, ContentsJson)
        },  _cts.Token);

        var duration = MetricsService.Histogram($"{MetricPrefix}_Duration", "Elapsed time for a chat message");
        var startedAt = DateTimeOffset.UtcNow;
        long? firstTokenMs = null;
        var updates = new List<ChatResponseUpdate>();

        try
        {
            await foreach (var update in ChatClient.GetStreamingResponseAsync(Messages, Options, _cts.Token))
            {
                updates.Add(update);
                var offsetMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            MetricsService.Count($"{MetricPrefix}_ToolCall", "Number of tool calls performed", ("Tool", call.Name));
                            break;
                        case FunctionResultContent:
                            MetricsService.Count($"{MetricPrefix}_ToolResult", "Number of tool results retrieved");
                            break;
                    }
                }

                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                firstTokenMs ??= offsetMs;
                StreamingText += update.Text;
                await InvokeAsync(StateHasChanged);
            }

            var response = updates.ToChatResponse();
            var responseDuration = (long)duration.ObserveDuration().TotalMilliseconds;

            if (response.ModelId is not null)
            {
                MetricsService.Count($"{MetricPrefix}_Send", "Number of chats sent", ("Model", response.ModelId), (MetricConstants.MetricsResultLabelName, MetricConstants.MetricsResultSuccessLabelValue));
            }
            else
            {
                MetricsService.Count($"{MetricPrefix}_Send", "Number of chats sent");
            }

            foreach (var newMessage in response.Messages)
            {
                Messages.Add(newMessage);
                var isAssistant = newMessage.Role == ChatRole.Assistant;
                await ChatRepository.AppendMessageAsync(_currentChat!.Id, new Data.Entities.ChatMessage
                {
                    Role = newMessage.Role.Value,
                    Content = newMessage.Text,
                    ContentsJson = JsonSerializer.Serialize(newMessage.Contents, ContentsJson),
                    ResponseId = isAssistant ? response.ResponseId : null,
                    ModelId = isAssistant ? response.ModelId : null,
                    FinishReason = isAssistant ? response.FinishReason?.Value : null,
                    InputTokens = isAssistant ? response.Usage?.InputTokenCount : null,
                    OutputTokens = isAssistant ? response.Usage?.OutputTokenCount : null,
                    TotalTokens = isAssistant ? response.Usage?.TotalTokenCount : null,
                    DurationMs = isAssistant ? responseDuration : null,
                    TimeToFirstTokenMs = isAssistant ? firstTokenMs : null
                }, _cts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogError(ex, "SendAsync cancelled");
            MetricsService.Count($"{MetricPrefix}_Send", "Number of chats sent", (MetricConstants.MetricsResultLabelName, MetricConstants.MetricsResultFailedLabelValue));
        }
        finally
        {
            StreamingText = string.Empty;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;

            await RefreshChatListAsync();
        }
    }

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();
        if (trimmed.Length <= 60)
        {
            return trimmed;
        }

        return trimmed[..60].TrimEnd() + "…";
    }

    private static string FormatArgs(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
        {
            return "";
        }

        return JsonSerializer.Serialize(args, ContentsJson);
    }

    private static string FormatResult(object? result)
    {
        return result switch
        {
            null => "null",
            string s => s,
            _ => JsonSerializer.Serialize(result, ContentsJson)
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _cts?.Cancel();
        _cts?.Dispose();
    }
}
