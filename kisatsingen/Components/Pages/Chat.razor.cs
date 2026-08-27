using System.Text.Json;
using kisatsingen.Data.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.AI;

namespace kisatsingen.Components.Pages;

public partial class Chat : ComponentBase, IDisposable
{
    private const string SystemPrompt = "You are a concise, helpful assistant. Use tools when they help.";

    private static readonly ChatOptions Options = new()
    {
        Tools = [ChatTools.GetCurrentTimeUtcTool]
    };

    private static readonly JsonSerializerOptions ContentsJson = AIJsonUtilities.DefaultOptions;
    private static readonly JsonSerializerOptions DebugJson = new() { WriteIndented = true };

    [Inject] private IChatClient ChatClient { get; set; } = default!;
    [Inject] private IChatRepository ChatRepository { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter] public Guid? ChatId { get; set; }

    private readonly List<ChatMessage> messages = new();
    private string messageText = string.Empty;
    private string streamingText = string.Empty;
    private bool isBusy;
    private CancellationTokenSource? cts;

    private readonly List<UpdateEntry> updateLog = new();
    private readonly List<ToolEntry> toolLog = new();
    private ResponseMeta? lastResponseMeta;

    private Data.Entities.Chat? currentChat;
    private Guid? currentChatId;
    private List<ChatSummary> chatList = new();

    private bool hasInitialized;

    private bool HasVisibleMessages =>
        messages.Any(m => m.Role != ChatRole.System) || streamingText.Length > 0;

    protected override async Task OnParametersSetAsync()
    {
        if (hasInitialized && currentChatId == ChatId) return;

        hasInitialized = true;
        currentChatId = ChatId;
        await LoadChatAsync();
        await RefreshChatListAsync();
    }

    private async Task LoadChatAsync()
    {
        messages.Clear();
        messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        streamingText = string.Empty;
        updateLog.Clear();
        toolLog.Clear();
        lastResponseMeta = null;
        currentChat = null;

        if (currentChatId is null) return;

        var chat = await ChatRepository.GetChatAsync(currentChatId.Value);
        if (chat is null)
        {
            currentChatId = null;
            Navigation.NavigateTo("/chat", replace: true);
            return;
        }

        currentChat = chat;
        foreach (var stored in chat.Messages)
        {
            messages.Add(RebuildMessage(stored));
        }
    }

    private static ChatMessage RebuildMessage(Data.Entities.ChatMessage stored)
    {
        var role = new ChatRole(stored.Role);
        if (!string.IsNullOrEmpty(stored.ContentsJson))
        {
            var contents = JsonSerializer.Deserialize<List<AIContent>>(stored.ContentsJson, ContentsJson);
            if (contents is not null && contents.Count > 0)
            {
                return new ChatMessage(role, contents);
            }
        }
        return new ChatMessage(role, stored.Content);
    }

    private async Task RefreshChatListAsync()
    {
        var list = await ChatRepository.ListChatsAsync(ownerId: null);
        chatList = list.ToList();
    }

    private async Task StartNewChatAsync()
    {
        if (isBusy) return;
        currentChatId = null;
        currentChat = null;
        await LoadChatAsync();
        await RefreshChatListAsync();
        Navigation.NavigateTo("/chat", replace: true);
    }

    private async Task OnComposerKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (isBusy || string.IsNullOrWhiteSpace(messageText)) return;

        var userText = messageText.Trim();
        messageText = string.Empty;
        var userMessage = new ChatMessage(ChatRole.User, userText);
        messages.Add(userMessage);
        isBusy = true;
        streamingText = string.Empty;
        cts = new CancellationTokenSource();

        updateLog.Clear();
        toolLog.Clear();
        lastResponseMeta = null;

        if (currentChat is null)
        {
            currentChat = await ChatRepository.CreateChatAsync(ownerId: null, title: BuildTitle(userText));
            currentChatId = currentChat.Id;
            Navigation.NavigateTo($"/chat/{currentChat.Id}", replace: true);
        }

        await ChatRepository.AppendMessageAsync(currentChat.Id, new Data.Entities.ChatMessage
        {
            Role = ChatRole.User.Value,
            Content = userText,
            ContentsJson = JsonSerializer.Serialize<IList<AIContent>>(userMessage.Contents, ContentsJson)
        });

        var startedAt = DateTimeOffset.UtcNow;
        long? firstTokenMs = null;
        var updateCount = 0;
        var updates = new List<ChatResponseUpdate>();

        try
        {
            await foreach (var update in ChatClient.GetStreamingResponseAsync(messages, Options, cts.Token))
            {
                updates.Add(update);
                updateCount++;
                var offsetMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            toolLog.Add(new ToolEntry("call", $"{call.Name}({FormatArgs(call.Arguments)})"));
                            break;
                        case FunctionResultContent result:
                            toolLog.Add(new ToolEntry("result", $"{result.CallId} → {FormatResult(result.Result)}"));
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    firstTokenMs ??= offsetMs;
                    streamingText += update.Text;
                    updateLog.Add(new UpdateEntry(offsetMs, update.Text));
                    await InvokeAsync(StateHasChanged);
                }
            }

            var response = updates.ToChatResponse();
            var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            lastResponseMeta = new ResponseMeta(
                ResponseId: response.ResponseId,
                ModelId: response.ModelId,
                FinishReason: response.FinishReason?.Value,
                UpdateCount: updateCount,
                CharCount: streamingText.Length,
                DurationMs: durationMs,
                TimeToFirstTokenMs: firstTokenMs,
                InputTokens: response.Usage?.InputTokenCount,
                OutputTokens: response.Usage?.OutputTokenCount,
                TotalTokens: response.Usage?.TotalTokenCount);

            foreach (var newMessage in response.Messages)
            {
                messages.Add(newMessage);
                var isAssistant = newMessage.Role == ChatRole.Assistant;
                await ChatRepository.AppendMessageAsync(currentChat!.Id, new Data.Entities.ChatMessage
                {
                    Role = newMessage.Role.Value,
                    Content = newMessage.Text ?? string.Empty,
                    ContentsJson = JsonSerializer.Serialize<IList<AIContent>>(newMessage.Contents, ContentsJson),
                    ResponseId = isAssistant ? response.ResponseId : null,
                    ModelId = isAssistant ? response.ModelId : null,
                    FinishReason = isAssistant ? response.FinishReason?.Value : null,
                    InputTokens = isAssistant ? response.Usage?.InputTokenCount : null,
                    OutputTokens = isAssistant ? response.Usage?.OutputTokenCount : null,
                    TotalTokens = isAssistant ? response.Usage?.TotalTokenCount : null,
                    DurationMs = isAssistant ? durationMs : null,
                    TimeToFirstTokenMs = isAssistant ? firstTokenMs : null
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            streamingText = string.Empty;
            isBusy = false;
            cts?.Dispose();
            cts = null;

            await RefreshChatListAsync();
        }
    }

    private static string BuildTitle(string userText)
    {
        var trimmed = userText.Trim();
        if (trimmed.Length <= 60) return trimmed;
        return trimmed[..60].TrimEnd() + "…";
    }

    private static string FormatArgs(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0) return "";
        return JsonSerializer.Serialize(args, ContentsJson);
    }

    private static string FormatResult(object? result)
    {
        if (result is null) return "null";
        if (result is string s) return s;
        return JsonSerializer.Serialize(result, ContentsJson);
    }

    private void ClearDebug()
    {
        updateLog.Clear();
        toolLog.Clear();
        lastResponseMeta = null;
    }

    private string FormatMessages()
    {
        var view = messages.Select(m => new
        {
            role = m.Role.Value,
            authorName = m.AuthorName,
            text = m.Text,
            contentTypes = m.Contents.Select(c => c.GetType().Name).ToArray()
        });
        return JsonSerializer.Serialize(view, DebugJson);
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
    }

    private sealed record UpdateEntry(long OffsetMs, string Text);
    private sealed record ToolEntry(string Kind, string Text);

    private sealed record ResponseMeta(
        string? ResponseId,
        string? ModelId,
        string? FinishReason,
        int UpdateCount,
        int CharCount,
        long DurationMs,
        long? TimeToFirstTokenMs,
        long? InputTokens,
        long? OutputTokens,
        long? TotalTokens);
}
