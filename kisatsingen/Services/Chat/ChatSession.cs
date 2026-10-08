using kisatsingen.AIFunctions.FileTools;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Processing;
using Microsoft.JSInterop;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.Chat;

// What the page shows of the open chat. Running a turn is TurnRunner's job.
public sealed class ChatSession : IAsyncDisposable
{
    private const string DefaultSystemPrompt = "You are a concise, helpful assistant. Use tools when they help.";

    private static readonly string MetricPrefix = $"{MetricConstants.MetricsAppPrefix}_ChatSession";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatModelCatalog _catalog;
    private readonly IChatRepository _repo;
    private readonly ChatClientChannel _channel;
    private readonly TurnRunner _runner;
    private readonly ChatAttachments _attachments;
    private readonly IKnowledgeFileRepository _knowledgeFileRepository;
    private readonly ILogger<ChatSession> _logger;

    private readonly List<Turn> _turns = [];

    // Not derivable from status: a Running turn may have been loaded that way.
    private Guid? _liveTurnId;

    // Identity only; ChatManager owns the chat's metadata.
    private Guid? _currentChatId;
    private string _effectiveSystemPrompt = DefaultSystemPrompt;

    // Loaded with the chat. Every file a turn makes available is added as it
    // becomes available, while the view is still that turn's. A file another
    // tab saves shows once the chat is opened again; that lag is safe, since
    // duplicates are checked against the database.
    private IReadOnlyList<KnowledgeFileMetadata> _knowledgeFiles = [];

    // Snapshotted by SendAsync, so switching mid-stream only affects the next turn.
    private ChatModel _selectedModel;

    // Null between turns, which is what makes a late Cancel() a harmless no-op.
    private TurnCancellation? _turnCancellation;

    private Task _turnCompletion = Task.CompletedTask;

    // Bumped whenever the view switches chat. Anything that awaited across a bump
    // no longer owns the view and must leave it alone.
    private int _viewVersion;

    public event Action? StateChanged;

    // Mid-turn, so the page can put a new chat's id in the URL before the answer ends.
    public event Action<Guid>? ChatCreated;

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
        KnowledgeFileProcessingQueue processingQueue,
        TempFileStore tempFiles,
        FileToolFactory fileTools)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _selectedModel = catalog.Default;
        _repo = repo;
        _attachments = attachments;
        _knowledgeFileRepository = knowledgeFileRepository;
        _logger = logger;

        _channel = new ChatClientChannel(js, logger);
        var streamer = new TurnStreamer(_channel, metrics, MetricPrefix);
        var attachmentStep = new TurnAttachmentStep(processingQueue, knowledgeFileRepository, tempFiles, logger);
        _runner = new TurnRunner(authenticationService, catalog, repo, chatManager, metrics, streamer, MetricPrefix, logger, userTokenUsageRepository, attachmentStep, fileTools);
    }

    public Guid? ChatId => _currentChatId;
    public IReadOnlyList<KnowledgeFileMetadata> KnowledgeFiles => _knowledgeFiles;
    public bool IsBusy { get; private set; }
    public bool HasVisibleMessages => _turns.Count > 0;
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
                _currentChatId,
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

    // Estimated from the request we would send, not from usage: usage sums every
    // tool round trip. Right for cost (see ConversationUsage), wrong for size.
    public long? EstimatedContextTokens =>
        _turns.Count == 0
            ? null
            : ContextTokenEstimator.Estimate(TranscriptRequest.Build(_turns), _effectiveSystemPrompt);

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

    public async Task LoadAsync(Guid? chatId, CancellationToken ct = default)
    {
        var viewVersion = ++_viewVersion;

        // Leaving a chat stops its turn, and the turn winds down into its own chat
        // before this one is shown.
        await StopTurnForLeaveAsync();
        if (!OwnsView(viewVersion))
        {
            return;
        }

        ClearView();

        if (chatId is Guid id)
        {
            var userObjectId = await _authenticationService.RequireUserObjectIdentifierAsync();
            var chat = await _repo.GetChatAsync(userObjectId, id, ct);
            var knowledgeFiles = chat is null ? [] : await _knowledgeFileRepository.ListFilesForChatAsync(userObjectId, id, ct);

            // A later navigation that finished first must not be overwritten.
            if (!OwnsView(viewVersion))
            {
                return;
            }

            if (chat is not null)
            {
                _currentChatId = chat.Id;
                _effectiveSystemPrompt = chat.SystemPrompt ?? DefaultSystemPrompt;
                _knowledgeFiles = knowledgeFiles;
                var loadedAt = DateTimeOffset.UtcNow;
                _turns.AddRange(chat.Turns.Select(stored => ChatTurnMapper.FromEntity(stored, loadedAt, ResolveModelName, _logger)));

                // Continuing a chat must not silently change who answers it.
                _selectedModel = FindLastModel() ?? _catalog.Default;
            }
        }

        Notify();
    }

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
            _currentChatId,
            storedKey,
            _catalog.Default.Key);

        return null;
    }

    // A key can outlive its model; the raw key still beats a blank or wrong name.
    private string ResolveModelName(ChatModelKey key) =>
        _catalog.TryGet(key, out var model) ? model.DisplayName : key.Value;

    // For when the open chat was just deleted: there is nothing to load.
    public void Reset()
    {
        _viewVersion++;
        ClearView();
        Notify();
    }

    // A turn stops when the session is asked to show another chat, never when a
    // route without a chat is opened: there it finishes and is waiting on return.
    public Task StopTurnForLeaveAsync()
    {
        _turnCancellation?.CancelForLeave();
        return _turnCompletion;
    }

    private void ClearView()
    {
        _turns.Clear();
        _liveTurnId = null;
        _currentChatId = null;
        _effectiveSystemPrompt = DefaultSystemPrompt;
        _knowledgeFiles = [];
        _selectedModel = _catalog.Default;

        // Attached to the chat being left, not the one being opened.
        _attachments.Reset();
    }

    private bool OwnsView(int viewVersion) => viewVersion == _viewVersion;

    public async Task SendAsync(string text)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text))
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

        // The turn keeps writing to its own chat after the user moves on; only the
        // page stops following it.
        var viewVersion = _viewVersion;
        var isNewChat = _currentChatId is null;

        // A turn that started on one model and prompt must finish on them.
        var model = _selectedModel;
        var turn = Turn.Start(text.Trim(), model, _effectiveSystemPrompt);
        var input = new TurnInput(turn, _currentChatId, [.. _turns], model, _attachments.AttachReadyToMessage(), ChatHasKnowledgeFiles());

        _turns.Add(turn);
        _liveTurnId = turn.Id;
        Notify();

        try
        {
            var observer = new TurnObserver(
                chatId => OnChatPersisted(viewVersion, chatId, isNewChat),
                snapshot => ShowSnapshot(viewVersion, snapshot),
                file => AddKnowledgeFile(viewVersion, file));

            var ended = await _runner.RunAsync(input, cancellation, observer);
            Replace(viewVersion, ended);
        }
        finally
        {
            // Nested so a throwing StateChanged subscriber can't leave LoadAsync
            // waiting on this turn forever.
            try
            {
                if (OwnsView(viewVersion))
                {
                    _liveTurnId = null;
                }
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

    private void OnChatPersisted(int viewVersion, Guid chatId, bool isNewChat)
    {
        if (!OwnsView(viewVersion))
        {
            return;
        }

        _currentChatId = chatId;
        if (isNewChat)
        {
            ChatCreated?.Invoke(chatId);
        }
    }

    // By id, since a turn reports a file it reused, which may already be listed.
    private void AddKnowledgeFile(int viewVersion, KnowledgeFileMetadata file)
    {
        if (!OwnsView(viewVersion) || _knowledgeFiles.Any(known => known.Id == file.Id))
        {
            return;
        }

        _knowledgeFiles = [.. _knowledgeFiles, file];
        Notify();
    }

    private void ShowSnapshot(int viewVersion, Turn snapshot)
    {
        if (Replace(viewVersion, snapshot))
        {
            Notify();
        }
    }

    // By id: a view that has moved on no longer holds the turn, so nothing matches.
    private bool Replace(int viewVersion, Turn turn)
    {
        if (!OwnsView(viewVersion))
        {
            return false;
        }

        var index = _turns.FindIndex(candidate => candidate.Id == turn.Id);
        if (index < 0)
        {
            return false;
        }

        _turns[index] = turn;
        return true;
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
