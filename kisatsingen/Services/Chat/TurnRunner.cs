using kisatsingen.AIFunctions;
using kisatsingen.AIFunctions.KnowledgeFileTools;
using kisatsingen.AIFunctions.WebSearch;
using kisatsingen.Constants;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Reading;
using Microsoft.Extensions.AI;
using Vestfold.Extensions.Metrics.Services;

namespace kisatsingen.Services.Chat;

internal sealed record TurnObserver(
    Action TurnSaved,
    Action<Turn> Changed,
    Action<KnowledgeFileMetadata> KnowledgeFileAvailable);

// Knows nothing of the view; ChatSession decides what a snapshot means for the page.
//
// One write path for every ending: inserted as Running before the model is asked,
// updated once however it ends. So a crash mid-turn still leaves a row that says
// the turn never finished.
internal sealed class TurnRunner
{
    private const string TurnIdPropertyName = "TurnId";
    private const string ChatIdPropertyName = "ChatId";

    private readonly IAuthenticationService _authenticationService;
    private readonly IChatModelCatalog _catalog;
    private readonly IChatRepository _repo;
    private readonly ChatManager _chatManager;
    private readonly IMetricsService _metrics;
    private readonly TurnStreamer _streamer;
    private readonly string _metricPrefix;
    private readonly ILogger _logger;
    private readonly IUserUsageRepository _userUsageRepository;
    private readonly TurnAttachmentProcessor _attachmentProcessor;
    private readonly KnowledgeFileToolFactory _knowledgeFileTools;
    private readonly SearchWebToolFactory _searchWebTools;

    public TurnRunner(
        IAuthenticationService authenticationService,
        IChatModelCatalog catalog,
        IChatRepository repo,
        ChatManager chatManager,
        IMetricsService metrics,
        TurnStreamer streamer,
        string metricPrefix,
        ILogger logger,
        IUserUsageRepository userUsageRepository,
        TurnAttachmentProcessor attachmentProcessor,
        KnowledgeFileToolFactory knowledgeFileTools,
        SearchWebToolFactory searchWebTools)
    {
        _authenticationService = authenticationService;
        _catalog = catalog;
        _repo = repo;
        _chatManager = chatManager;
        _metrics = metrics;
        _streamer = streamer;
        _metricPrefix = metricPrefix;
        _logger = logger;
        _userUsageRepository = userUsageRepository;
        _attachmentProcessor = attachmentProcessor;
        _knowledgeFileTools = knowledgeFileTools;
        _searchWebTools = searchWebTools;
    }

    // Throws only what the page must handle itself: an unauthenticated caller, an
    // allocation failure, and a metrics failure, surfaced rather than swallowed.
    public async Task<Turn> RunAsync(TurnInput input, TurnCancellation cancellation, TurnObserver observer)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            [TurnIdPropertyName] = input.Turn.Id,
            [ChatIdPropertyName] = input.ChatId,
        });

        var model = input.Model;
        var webSearch = _searchWebTools.StartTurn(access: input.ToolDecision.WebSearchAccess);
        var row = new TurnRow(input.ChatId, input.AssistantId, input.IsChatSaved, webSearch);

        // Counted once, in the finally, so every send lands on exactly one outcome.
        // Starts as Failed so an exception nothing classified lands in the alerted bucket.
        var outcome = TurnOutcome.Failed;
        string? servedModelId = null;

        try
        {
            var attempt = await ExecuteAsync(row, input, cancellation, observer);
            outcome = attempt.Outcome;
            servedModelId = attempt.ServedModelId;

            await FinishAttemptAsync(row, attempt, model);
            return attempt.Turn;
        }
        // Rethrown: with no error boundary it ends the circuit, and the reload is what
        // the sign-in middleware redirects. Practically unreachable mid-chat: a
        // circuit's sign-in state is fixed when it starts.
        catch (UserNotAuthenticatedException)
        {
            outcome = TurnOutcome.Unauthenticated;
            throw;
        }
        finally
        {
            CountSend(outcome, servedModelId ?? model.ModelId, model.Provider);
        }
    }

    // The catches only name the outcome and never act on it, so none can throw past it.
    private async Task<TurnAttempt> ExecuteAsync(
        TurnRow row,
        TurnInput input,
        TurnCancellation cancellation,
        TurnObserver observer)
    {
        var turn = input.Turn;
        var builder = new TurnBuilder(turn);
        var stage = TurnStage.Authenticating;

        try
        {
            var ownerId = await _authenticationService.RequireUserObjectIdentifierAsync();
            row.OwnerId = ownerId;

            stage = TurnStage.SavingMessage;
            if (await InsertAsync(row, ownerId, turn, observer, cancellation.Token) is not { } inserted)
            {
                _logger.LogInformation("Chat {ChatId} no longer exists, so the message sent in it was not saved", row.ChatId);
                return new TurnAttempt(builder.Finish(TurnStatus.ChatDeleted));
            }

            stage = TurnStage.ProcessingAttachments;
            await ProcessAttachmentsAsync(inserted, input.SentAttachments.All, builder, observer, cancellation.Token);

            stage = TurnStage.Generating;
            await StreamAsync(inserted, input, row.WebSearch, builder, observer, cancellation.Token);

            // CancellationToken.None: the answer is complete, so a stop or a switch of
            // chat during this one write must not store it as stopped.
            stage = TurnStage.SavingResponse;
            var answered = builder.Finish(TurnStatus.Completed);
            await SaveAsync(inserted, answered, CancellationToken.None);

            // Only after the write: a turn is a success once its answer is stored.
            return new TurnAttempt(answered);
        }
        // The filter is load-bearing: a provider timeout is also an
        // OperationCanceledException, and without it outages would be filed as
        // user stops, invisible to failure alerts.
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            var status = cancellation switch
            {
                { IsDisconnect: true } => TurnStatus.Disconnected,
                { IsLeave: true } => TurnStatus.LeftChat,
                _ => TurnStatus.Stopped
            };

            return new TurnAttempt(builder.Finish(status));
        }
        // Swallowed, bugs included: they are logged whole, and taking the circuit
        // down would only cost the user their transcript. Nothing that is control
        // flow — Blazor's NavigationException above all — may ever be thrown in the try.
        //
        // Two pass through: an unauthenticated caller, which RunAsync counts, and an
        // allocation failure, which isn't this turn's fault and fails a retry the same
        // way. The latter is kept off the Failure counter, so the two don't reconcile.
        catch (Exception ex) when (ex is not (UserNotAuthenticatedException or OutOfMemoryException))
        {
            return new TurnAttempt(builder.Finish(TurnStatus.Failed, stage), ex);
        }
    }

    // Saved before counting: a metrics failure surfaces, and must not also leave the
    // stored turn unfinished.
    private async Task FinishAttemptAsync(TurnRow row, TurnAttempt attempt, ChatModel model)
    {
        LogFailure(row, attempt);

        if (attempt.ShouldSaveTurn)
        {
            await SaveEndedTurnAsync(row, attempt.Turn);
        }

        await InsertUsageRecordAsync(row, attempt, model);
        await InsertWebSearchUsageRecordAsync(row, attempt);

        LogEnded(row, attempt, model);
        CountFailure(attempt);
    }

    private void LogFailure(TurnRow row, TurnAttempt attempt)
    {
        if (attempt is { Failure: { } error, Turn.FailedAt: { } stage })
        {
            _logger.LogError(error, "Chat turn failed during {Stage} for chat {ChatId}", stage, row.ChatId);
        }
    }

    // One line for every ending, so the logs show what users do and not only what
    // fails. Tool names only: a call's arguments carry what the user asked.
    private void LogEnded(TurnRow row, TurnAttempt attempt, ChatModel model)
    {
        var turn = attempt.Turn;
        var toolCalls = turn.Answer.OfType<ToolCall>().ToList();
        var usage = turn.Metadata?.Usage;

        _logger.LogInformation(
            "Chat turn ended as {Status} (failed stage: {FailedAt}) on model {ModelKey} with assistant {AssistantId}: " +
            "{AttachmentCount} attachments ({UnavailableAttachmentCount} unavailable), tools {ToolNames} ({FailedToolCallCount} failed), " +
            "web search allowed {IsWebSearchAllowed}, {DurationMs} ms ({TimeToFirstTokenMs} ms to first token), " +
            "{InputTokens} input and {OutputTokens} output tokens",
            turn.Status,
            turn.FailedAt,
            model.Key.Value,
            row.AssistantId,
            turn.Attachments.Count,
            turn.Attachments.Count(attachment => attachment.UnavailableReason is not null),
            toolCalls.Select(toolCall => toolCall.ToolName).ToArray(),
            toolCalls.Count(toolCall => toolCall.Status == ToolStatus.Failed),
            turn.IsWebSearchAllowed,
            turn.Metadata?.DurationMs,
            turn.Metadata?.TimeToFirstTokenMs,
            usage?.CombinedInputTokens,
            usage?.CombinedOutputTokens);
    }

    private void CountFailure(TurnAttempt attempt)
    {
        if (attempt is { Failure: { } error, Turn.FailedAt: { } stage })
        {
            _metrics.Count(
                $"{_metricPrefix}_failure_total",
                "Failed chat turns, by stage and exception type",
                (MetricConstants.MetricsStageLabelName, TurnStageMetric.LabelValue(stage)),
                (MetricConstants.MetricsExceptionLabelName, error.GetType().Name));
        }
    }

    // Sets row.Inserted before reporting the turn saved, so a listener that throws
    // cannot leave a written turn that the ending path never updates. Null when the
    // chat no longer exists, and then nothing is written.
    private async Task<InsertedTurn?> InsertAsync(TurnRow row, Guid ownerId, Turn turn, TurnObserver observer, CancellationToken ct)
    {
        // Stopped before anything was saved: keep it that way.
        ct.ThrowIfCancellationRequested();

        var isNewChat = !row.IsChatSaved;

        try
        {
            if (isNewChat)
            {
                await _chatManager.CreateChatAsync(row.ChatId, turn.Prompt, row.AssistantId, ct);
            }

            if (!await _repo.InsertTurnAsync(ownerId, row.ChatId, ChatTurnMapper.ToEntity(turn), ct))
            {
                return null;
            }
        }
        // If saving the first message fails at any step, the new chat is deleted, so it
        // never opens empty. The next message creates the chat again under the same id.
        catch (Exception) when (isNewChat)
        {
            await DiscardChatAsync(row.ChatId);
            throw;
        }

        var inserted = new InsertedTurn(ownerId, row.ChatId, turn.Id, row.AssistantId);
        row.Inserted = inserted;
        observer.TurnSaved();
        return inserted;
    }

    // Swallows failures: the turn's own failure is the one to report.
    private async Task DiscardChatAsync(Guid chatId)
    {
        try
        {
            await _chatManager.DeleteAsync(chatId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete chat {ChatId} after its first message failed to save. If the chat was created, it will show as an empty chat.", chatId);
        }
    }

    // One file at a time, in the order they were attached, each shown as it
    // settles so a stop or a later failure of the turn cannot lose it. A stop
    // leaves the rest processing, and ending the turn marks them interrupted.
    private async Task ProcessAttachmentsAsync(
        InsertedTurn inserted,
        IReadOnlyList<ReadyAttachment> sentAttachments,
        TurnBuilder builder,
        TurnObserver observer,
        CancellationToken ct)
    {
        for (var index = 0; index < sentAttachments.Count; index++)
        {
            var result = await _attachmentProcessor.ProcessAsync(inserted.OwnerId, inserted.ChatId, inserted.TurnId, inserted.AssistantId, sentAttachments[index], ct);

            builder.SettleAttachment(index, result);
            if (result is TurnAttachmentResult.Available available)
            {
                observer.KnowledgeFileAvailable(available.File);
            }

            observer.Changed(builder.Snapshot());
        }
    }

    // Built here, after the attachments settle, since the attachment line
    // needs each saved file's id.
    private Task StreamAsync(
        InsertedTurn inserted,
        TurnInput input,
        TurnWebSearch? webSearch,
        TurnBuilder builder,
        TurnObserver observer,
        CancellationToken ct)
    {
        var turn = input.Turn;
        var messages = TranscriptRequest.Build([.. input.EarlierTurns, builder.Snapshot()]);
        builder.SetRequestTokens(ContextTokenEstimator.Estimate(messages, turn.SystemPrompt));

        var runtime = _catalog.Resolve(input.Model.Key);

        // The snapshot the turn is stored with, so the record can't drift from what was sent.
        var options = runtime.CreateOptions();
        options.Instructions = turn.SystemPrompt;
        options.Tools = BuildToolList(input.ToolDecision, inserted, webSearch);

        return _streamer.StreamAsync(runtime.Client, messages, options, builder, () => observer.Changed(builder.Snapshot()), ct);
    }

    private List<AITool> BuildToolList(TurnToolDecision decision, InsertedTurn inserted, TurnWebSearch? webSearch)
    {
        var toolList = new List<AITool>();

        if (decision.ShouldOfferFileGenerationTools)
        {
            toolList.AddRange(FileGenerationTools.All);
        }

        if (decision.ShouldOfferKnowledgeFileTools)
        {
            var scope = new KnowledgeFileScope(inserted.OwnerId, inserted.ChatId, inserted.AssistantId);
            toolList.AddRange(_knowledgeFileTools.Create(scope));
        }

        if (webSearch is not null)
        {
            toolList.Add(webSearch.Tool);
        }

        return toolList;
    }

    private async Task SaveAsync(InsertedTurn inserted, Turn ended, CancellationToken ct)
    {
        await _repo.UpdateTurnAsync(inserted.OwnerId, inserted.ChatId, ChatTurnMapper.ToEntity(ended), ct);
        _chatManager.MarkTouched(inserted.ChatId, DateTimeOffset.UtcNow);
    }

    // CancellationToken.None: after a stop the turn's own is cancelled and would
    // abort the write that records it. Swallows failures so a lost stored copy
    // cannot replace the outcome the user is shown.
    private async Task SaveEndedTurnAsync(TurnRow row, Turn ended)
    {
        var inserted = row.Inserted;
        if (inserted is null)
        {
            return;
        }

        try
        {
            await SaveAsync(inserted, ended, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save how turn {TurnId} in chat {ChatId} ended ({Status}). It reads back as unfinished.",
                ended.Id,
                inserted.ChatId,
                ended.Status);
        }
    }

    private async Task InsertUsageRecordAsync(TurnRow row, TurnAttempt attempt, ChatModel model)
    {
        var usage = attempt.Turn.Metadata?.Usage;

        if (usage is null)
        {
            return;
        }

        try
        {
            var userId = row.OwnerId ?? await _authenticationService.RequireUserObjectIdentifierAsync();
            var newRecord = NewUsageRecord.ForTurn(
                userId: userId,
                chatId: row.ChatId,
                turnId: attempt.Turn.Id,
                assistantId: row.AssistantId,
                provider: model.Provider,
                modelId: model.ModelId,
                usage: usage,
                status: attempt.Turn.Status);
            await _userUsageRepository.InsertUsageRecordAsync(newRecord, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save token usage for turn {TurnId} in chat {ChatId} with status {Status}, so {Usage} is missing from the usage totals. " +
                "Check the exception and the database.",
                attempt.Turn.Id,
                row.ChatId,
                attempt.Turn.Status,
                usage);
        }
    }

    // One row for all the turn's searches, written on every ending like the
    // turn's own row, so searches finished before a stop are recorded.
    private async Task InsertWebSearchUsageRecordAsync(TurnRow row, TurnAttempt attempt)
    {
        if (row.WebSearch?.Quota is not { } quota)
        {
            return;
        }

        var usage = quota.Usage;
        if (usage is null && quota.BilledSearches == 0)
        {
            return;
        }

        try
        {
            var userId = row.OwnerId ?? await _authenticationService.RequireUserObjectIdentifierAsync();
            var newRecord = NewUsageRecord.ForWebSearch(
                userId: userId,
                chatId: row.ChatId,
                turnId: attempt.Turn.Id,
                assistantId: row.AssistantId,
                provider: quota.Provider,
                modelId: quota.ModelId,
                usage: usage ?? new MessageUsage(InputTokens: null, OutputTokens: null),
                webSearchCount: quota.BilledSearches);
            await _userUsageRepository.InsertUsageRecordAsync(newRecord, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save web search usage for turn {TurnId} in chat {ChatId}, so {WebSearchCount} billed searches and {Usage} are missing from the usage totals. " +
                "Check the exception and the database.",
                attempt.Turn.Id,
                row.ChatId,
                quota.BilledSearches,
                usage);
        }
    }

    // One place builds the labels because Prometheus fixes a metric's label names
    // on first use and throws if a later call differs.
    private void CountSend(TurnOutcome outcome, string? modelId, string provider) =>
        _metrics.Count(
            $"{_metricPrefix}_send_total",
            "Chat send attempts, by outcome",
            (MetricConstants.MetricsModelLabelName, modelId ?? MetricConstants.MetricsModelUnknownLabelValue),
            (MetricConstants.MetricsProviderLabelName, provider),
            (MetricConstants.MetricsResultLabelName, TurnOutcomeMetric.LabelValue(outcome)));

    // What is known of the turn's row so far; a turn that ends early may never
    // learn the rest.
    private sealed class TurnRow(Guid chatId, Guid? assistantId, bool isChatSaved, TurnWebSearch? webSearch)
    {
        public Guid ChatId { get; } = chatId;

        public Guid? AssistantId { get; } = assistantId;

        // False for a new chat: it is saved with this turn.
        public bool IsChatSaved { get; } = isChatSaved;

        // Null when search is not configured.
        public TurnWebSearch? WebSearch { get; } = webSearch;

        // Null until sign-in is confirmed.
        public Guid? OwnerId { get; set; }

        // Null until the turn's row is written.
        public InsertedTurn? Inserted { get; set; }
    }

    // The turn's row and what it belongs to, once it is written.
    private sealed record InsertedTurn(Guid OwnerId, Guid ChatId, Guid TurnId, Guid? AssistantId);
}
