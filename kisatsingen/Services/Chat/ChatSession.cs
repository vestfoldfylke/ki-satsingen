using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Pipeline;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.Chat;

// What the page shows of the open chat. Running a turn is TurnRunner's job.
public sealed class ChatSession : IAsyncDisposable
{
    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_chat_session";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatModelCatalog _catalog;
    private readonly IChatRepository _repo;
    private readonly ChatClientChannel _channel;
    private readonly TurnRunner _runner;
    private readonly ChatAttachments _attachments;
    private readonly IKnowledgeFileRepository _knowledgeFileRepository;
    private readonly IAssistantRepository _assistantRepository;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<Turn> _turns = [];

    // Not derived from the turns' status: a turn opened from the database can be
    // Running because another tab is still answering it.
    private Guid? _liveTurnId;

    // Identity only; ChatManager owns the chat's metadata. A new chat has its id
    // from the start and is saved with its first turn; if that turn was stopped
    // or failed before saving, the next message saves it under the same id.
    private Guid _chatId;
    private ChatState _chatState = ChatState.New;

    private enum ChatState
    {
        New,

        // A chat is being loaded: the session holds neither a new chat nor that chat yet.
        Opening,

        Saved,
        NotFound
    }

    private IReadOnlyList<KnowledgeFileMetadata> _knowledgeFiles = [];

    // Read when the chat is opened or started, so an edit to the assistant applies
    // the next time the chat is opened. Null for a plain chat, and for a chat
    // whose assistant the user can no longer use, which goes on as a plain chat.
    private Assistant? _assistant;

    // Snapshotted by SendAsync, so switching mid-stream only affects the next turn.
    private ChatModel _selectedModel;

    // Null between turns, which is what makes a late Cancel() a harmless no-op.
    private TurnCancellation? _turnCancellation;

    private Task _turnCompletion = Task.CompletedTask;

    // Increased by every chat request (see RequestChat).
    private int _chatRequestCount;

    // The chat asked for last: shown, still loading, or not found. Unlike _chatId,
    // it is set as soon as the chat is asked for, and it is never used as a chat's id.
    private Guid _requestedChatId;

    public event Action? StateChanged;

    public ChatSession(
        IAuthenticationService authenticationService,
        IChatModelCatalog catalog,
        IChatRepository repo,
        ChatManager chatManager,
        ChatAttachments attachments,
        IMetricsService metrics,
        IJSRuntime js,
        ILogger<ChatSession> logger,
        IUserTokenUsageRepository userTokenUsageRepository,
        IKnowledgeFileRepository knowledgeFileRepository,
        KnowledgeFilePipeline knowledgeFilePipeline,
        FileToolFactory fileTools,
        IAssistantRepository assistantRepository)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _selectedModel = catalog.Default;
        _chatId = Guid.NewGuid();
        _requestedChatId = _chatId;
        _repo = repo;
        _attachments = attachments;
        _knowledgeFileRepository = knowledgeFileRepository;
        _assistantRepository = assistantRepository;
        _logger = logger;

        _channel = new ChatClientChannel(js, logger);
        var streamer = new TurnStreamer(_channel, metrics, MetricPrefix);
        var attachmentProcessor = new TurnAttachmentProcessor(knowledgeFilePipeline, knowledgeFileRepository, logger);
        _runner = new TurnRunner(authenticationService, catalog, repo, chatManager, metrics, streamer, MetricPrefix, logger, userTokenUsageRepository, attachmentProcessor, fileTools);
    }

    public Guid ChatId => _chatId;

    // Set when OpenChatAsync was asked for a chat that doesn't exist or isn't the user's.
    public bool IsChatNotFound => _chatState == ChatState.NotFound;

    public bool IsNewChatWith(Guid? assistantId) =>
        _chatState == ChatState.New && _turns.Count == 0 && _assistant?.Id == assistantId;

    public bool CanSend => !IsBusy && _chatState is ChatState.New or ChatState.Saved;

    public IReadOnlyList<KnowledgeFileMetadata> KnowledgeFiles => _knowledgeFiles;
    public bool IsBusy { get; private set; }
    public bool HasLiveTurn => _liveTurnId is not null;

    public IReadOnlyList<TurnView> Transcript => TranscriptView.Build(_turns, _liveTurnId);

    public ChatModel SelectedModel => _selectedModel;

    // The allow-list. Every user gets every model today; per-user gating goes here.
    public IReadOnlyList<ChatModel> AvailableModels => _catalog.Models;

    // The key comes from the browser, so it is checked against the allow-list
    // rather than the catalogue — otherwise gating a model later has a way around.
    public Task SelectModelAsync(ChatModelKey key)
    {
        var model = AvailableModels.FirstOrDefault(candidate => candidate.Key == key);

        if (model is null)
        {
            _logger.LogWarning(
                "Refused to switch chat {ChatId} to model {ModelKey}: not in this user's available models.",
                _chatId,
                key);
            return Task.CompletedTask;
        }

        if (model.Key == _selectedModel.Key)
        {
            return Task.CompletedTask;
        }

        // State only, no I/O: that is what makes switching mid-stream safe.
        _selectedModel = model;
        Notify();
        return Task.CompletedTask;
    }

    // The one place the prompt for the next turn is chosen, so what is sent and
    // what is estimated can never differ.
    private string SystemPrompt =>
        _assistant is Assistant assistant
            ? ChatInstructions.ForAssistant(assistant.Name, assistant.Instructions)
            : ChatInstructions.ForPlainChat;

    // Estimated from the request we would send, not from usage: usage sums every
    // tool round trip. Right for cost (see ConversationUsage), wrong for size.
    public long? EstimatedContextTokens =>
        _turns.Count == 0
            ? null
            : ContextTokenEstimator.Estimate(TranscriptRequest.Build(_turns), SystemPrompt);

    public MessageUsage? ConversationUsage
    {
        get
        {
            var usages = _turns.Select(turn => turn.Metadata?.Usage).OfType<MessageUsage>().ToList();

            return usages.Count == 0
                ? null
                : new MessageUsage(
                    usages.Sum(usage => usage.InputTokens ?? 0),
                    usages.Sum(usage => usage.OutputTokens ?? 0),
                    usages.Sum(usage => usage.EstimatedInputTokens ?? 0),
                    usages.Sum(usage => usage.EstimatedOutputTokens ?? 0));
        }
    }

    public async Task OpenChatAsync(Guid chatId)
    {
        // Already asked for: opening the chat again would stop the answer being
        // written in it, or look up a missing chat again.
        if (chatId == _requestedChatId)
        {
            return;
        }

        var requestNumber = RequestChat(chatId);
        await StopTurnForLeaveAsync();
        if (HasAnotherChatBeenRequestedSince(requestNumber))
        {
            return;
        }

        ResetToNewChat(Guid.NewGuid());
        _chatState = ChatState.Opening;

        var userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
        var chat = await _repo.GetChatAsync(userObjectId, chatId);
        var knowledgeFiles = chat is null ? [] : await _knowledgeFileRepository.ListFilesForChatAsync(userObjectId, chatId);
        var assistant = chat?.AssistantId is Guid assistantId
            ? await _assistantRepository.GetAssistantAsync(userObjectId, assistantId)
            : null;

        if (HasAnotherChatBeenRequestedSince(requestNumber))
        {
            return;
        }

        if (chat is null)
        {
            _chatState = ChatState.NotFound;
            Notify();
            return;
        }

        _chatId = chat.Id;
        _chatState = ChatState.Saved;
        _knowledgeFiles = knowledgeFiles;
        _assistant = assistant;
        var loadedAt = DateTimeOffset.UtcNow;
        _turns.AddRange(chat.Turns.Select(stored => ChatTurnMapper.FromEntity(stored, loadedAt, ResolveModelName, _logger)));

        // Continuing a chat must not silently change who answers it.
        _selectedModel = FindLastModel() ?? _catalog.Default;

        Notify();
    }

    public async Task StartNewChatAsync(Assistant? assistant)
    {
        var newChatId = Guid.NewGuid();
        var requestNumber = RequestChat(newChatId);
        await StopTurnForLeaveAsync();
        if (HasAnotherChatBeenRequestedSince(requestNumber))
        {
            return;
        }

        ResetToNewChat(newChatId);
        _assistant = assistant;
        Notify();
    }

    // Ignored unless the chat is with this assistant, so an edit never gives a chat
    // an assistant it was not saved with.
    public void UpdateAssistant(Assistant assistant)
    {
        if (_assistant?.Id != assistant.Id)
        {
            return;
        }

        _assistant = assistant;
        Notify();
    }

    // Returns the request's number, for HasAnotherChatBeenRequestedSince.
    private int RequestChat(Guid chatId)
    {
        _requestedChatId = chatId;
        return ++_chatRequestCount;
    }

    // A slow load then stops instead of replacing a chat the user opened after it.
    private bool HasAnotherChatBeenRequestedSince(int requestNumber) => requestNumber != _chatRequestCount;

    // No turns, a blank key and a removed model all mean "no previous model".
    private ChatModel? FindLastModel()
    {
        if (_turns.LastOrDefault(turn => turn.ModelKey is not null)?.ModelKey is not { } storedKey)
        {
            return null;
        }

        if (_catalog.TryGet(storedKey, out var model))
        {
            return model;
        }

        _logger.LogInformation(
            "Chat {ChatId} was last used with model {ModelKey}, which is no longer in the catalogue. Falling back to {DefaultModelKey}.",
            _chatId,
            storedKey,
            _catalog.Default.Key);

        return null;
    }

    // A key can outlive its model; the raw key still beats a blank or wrong name.
    private string ResolveModelName(ChatModelKey key) =>
        _catalog.TryGet(key, out var model) ? model.DisplayName : key.Value;

    // Stops the answer that is being written, recorded as the user leaving the
    // chat, and returns a task that completes when the turn has fully ended.
    public Task StopTurnForLeaveAsync()
    {
        _turnCancellation?.CancelForLeave();
        return _turnCompletion;
    }

    // Only called once StopTurnForLeaveAsync has completed, so no turn is running
    // and a turn only ever updates the chat it was sent in.
    private void ResetToNewChat(Guid newChatId)
    {
        _turns.Clear();
        _liveTurnId = null;
        _chatId = newChatId;
        _chatState = ChatState.New;
        _knowledgeFiles = [];
        _assistant = null;
        _selectedModel = _catalog.Default;

        // Attached to the chat being left, not the one being opened.
        _attachments.Reset();
    }

    public async Task SendAsync(string text)
    {
        if (!CanSend || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsBusy = true;

        // The branches below read this local, never the field: the field is what
        // Cancel() reaches, and it is nulled before this turn's teardown is done.
        var cancellation = new TurnCancellation();
        _turnCancellation = cancellation;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _turnCompletion = completion.Task;

        // A turn that started on one model must finish on it.
        var model = _selectedModel;
        using var sentAttachments = _attachments.TakeReady();
        var turn = Turn.Start(text.Trim(), model, SystemPrompt, sentAttachments.FileNames);
        var input = new TurnInput(turn, _chatId, IsChatSaved: _chatState == ChatState.Saved, AssistantId: _assistant?.Id, [.. _turns], model, sentAttachments, ChatHasKnowledgeFiles());

        _turns.Add(turn);
        _liveTurnId = turn.Id;
        Notify();

        try
        {
            var observer = new TurnObserver(
                TurnSaved: () => _chatState = ChatState.Saved,
                Changed: ShowSnapshot,
                KnowledgeFileAvailable: AddKnowledgeFile);

            var ended = await _runner.RunAsync(input, cancellation, observer);
            UpdateTurn(ended);
        }
        finally
        {
            // Nested so a throwing StateChanged subscriber can't leave the next
            // chat request waiting on this turn forever.
            try
            {
                _liveTurnId = null;
                IsBusy = false;

                // The only place it is disposed. Nulled first, so a Cancel() arriving
                // now no-ops instead of reaching disposed sources.
                _turnCancellation = null;
                cancellation.Dispose();

                Notify();
            }
            finally
            {
                completion.SetResult();
            }
        }
    }

    private bool ChatHasKnowledgeFiles() => _knowledgeFiles.Count > 0;

    // By id, since a turn reports a file it reused, which may already be listed.
    private void AddKnowledgeFile(KnowledgeFileMetadata file)
    {
        if (_knowledgeFiles.Any(known => known.Id == file.Id))
        {
            return;
        }

        _knowledgeFiles = [.. _knowledgeFiles, file];
        Notify();
    }

    private void ShowSnapshot(Turn snapshot)
    {
        UpdateTurn(snapshot);
        Notify();
    }

    private void UpdateTurn(Turn latest)
    {
        var index = _turns.FindIndex(turn => turn.Id == latest.Id);
        _turns[index] = latest;
    }

    private void Notify() => StateChanged?.Invoke();

    public void Cancel() => _turnCancellation?.CancelForUser();

    // Same effect as Cancel; the reason decides whether the transcript says the
    // user stopped the turn or the connection did.
    public void CancelForDisconnect() => _turnCancellation?.CancelForDisconnect();

    // A transient disconnect Blazor recovers from: delivery pauses, the turn runs on.
    public void PauseDelivery() => _channel.Pause();

    public void ResumeDelivery() => _channel.Resume();

    // Circuit teardown is a disconnect, not a stop. Cancels without disposing: the
    // turn still reads its cancellation while it winds down, and SendAsync disposes it.
    public async ValueTask DisposeAsync()
    {
        if (_turnCancellation is { } turn)
        {
            await turn.CancelForDisconnectAsync();
        }
    }
}
