using kisatsingen.Services.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace kisatsingen.Services.Chat;

// The open chat view's attachments, for one circuit: picked in the composer,
// uploaded to temp files, and discarded when the view moves to another chat.
public sealed class ChatAttachments : IDisposable
{
    private readonly IAuthenticationService _authenticationService;
    private readonly PendingAttachmentRegistry _registry;
    private readonly AttachmentUploader _uploader;
    private readonly AttachmentOptions _options;
    private readonly ILogger<ChatAttachments> _logger;

    // Cached on first use: registry events can arrive from the sweep, where
    // there is no authentication state to ask.
    private string? _ownerId;

    // Cancelled when the view moves on, so its uploads stop with it.
    private CancellationTokenSource _viewCancellation = new();

    public ChatAttachments(
        IAuthenticationService authenticationService,
        PendingAttachmentRegistry registry,
        AttachmentUploader uploader,
        AttachmentOptions options,
        ILogger<ChatAttachments> logger)
    {
        _authenticationService = authenticationService;
        _registry = registry;
        _uploader = uploader;
        _options = options;
        _logger = logger;
        _registry.Changed += OnRegistryChanged;
    }

    public event Action? Changed;

    // Renewed on every view switch, and never sent to the browser.
    public Guid ComposerKey { get; private set; } = Guid.NewGuid();

    // About the selection as a whole, which no single attachment can carry.
    public string? SelectionNotice { get; private set; }

    public IReadOnlyList<PendingAttachment> Pending =>
        _ownerId is null ? [] : _registry.List(_ownerId, ComposerKey);

    // Holds send, and the picker: a new selection makes the files of the one
    // still uploading unreadable.
    public bool IsUploading =>
        Pending.Any(attachment => attachment.Status is AttachmentStatus.Waiting or AttachmentStatus.Uploading);

    public async Task UploadAsync(InputFileChangeEventArgs selection)
    {
        var ownerId = await EnsureOwnerAsync();

        // Checked before GetMultipleFiles, which throws above its limit.
        if (selection.FileCount > _options.MaxFilesPerSelection)
        {
            SelectionNotice = UploadRejections.TooManyInSelection(_options.MaxFilesPerSelection);
            Changed?.Invoke();
            return;
        }

        SelectionNotice = null;
        var composerKey = ComposerKey;
        var viewStop = _viewCancellation.Token;

        // All reserved up front, so the whole selection is listed at once and
        // over-limit files are refused before any bytes move.
        var reserved = new List<(UploadReservation Reservation, IBrowserFile File)>();
        foreach (var file in selection.GetMultipleFiles(_options.MaxFilesPerSelection))
        {
            if (_registry.Reserve(ownerId, composerKey, AttachmentFileName.Normalize(file.Name), file.Size) is { } reservation)
            {
                reserved.Add((reservation, file));
            }
        }

        // One at a time: they share the circuit's connection, so in parallel
        // they would only take turns on it.
        foreach (var (reservation, file) in reserved)
        {
            await UploadOneAsync(reservation, file, viewStop);
        }
    }

    // Never throws: this runs in a UI event handler, where an exception would
    // end the circuit.
    private async Task UploadOneAsync(UploadReservation reservation, IBrowserFile file, CancellationToken viewStop)
    {
        if (viewStop.IsCancellationRequested || !_registry.TryStartUpload(reservation.UploadId))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(reservation.Cancellation, viewStop);
        var progress = new ImmediateProgress(receivedBytes => _registry.ReportProgress(reservation.UploadId, receivedBytes));

        try
        {
            await using var source = file.OpenReadStream(_options.MaxFileBytes, linked.Token);
            var outcome = await _uploader.CopyAsync(source, reservation.Type, _options.MaxFileBytes, progress, linked.Token);

            switch (outcome)
            {
                case UploadOutcome.Stored stored:
                    _registry.Complete(reservation.UploadId, stored);
                    break;
                case UploadOutcome.Rejected rejected:
                    _registry.Reject(reservation.UploadId, rejected.Reason);
                    break;
            }
        }
        // Removed, or the view moved on: the registry has already forgotten it.
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        // A lost connection mid-upload surfaces as whatever the stream throws.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Upload {UploadId} failed after it started.", reservation.UploadId);
            _registry.Reject(reservation.UploadId, UploadRejections.Interrupted);
        }
    }

    public void Remove(Guid uploadId)
    {
        if (_ownerId is not null)
        {
            _registry.Remove(_ownerId, uploadId);
        }
    }

    // For a view switch: what was attached here is not the next chat's.
    public void Reset()
    {
        Discard();
        _viewCancellation = new CancellationTokenSource();
        ComposerKey = Guid.NewGuid();
        SelectionNotice = null;
        Changed?.Invoke();
    }

    private async Task<string> EnsureOwnerAsync() =>
        _ownerId ??= await _authenticationService.RequireUserObjectIdentifierAsync();

    // Cancelled, never disposed: an upload may still be linking to it.
    private void Discard()
    {
        _viewCancellation.Cancel();
        if (_ownerId is not null)
        {
            _registry.RemoveAll(_ownerId, ComposerKey);
        }
    }

    private void OnRegistryChanged(Guid scopeKey)
    {
        if (scopeKey == ComposerKey)
        {
            Changed?.Invoke();
        }
    }

    // The circuit is gone, so nothing can send what is pending.
    public void Dispose()
    {
        _registry.Changed -= OnRegistryChanged;
        Discard();
    }

    // Progress<T> posts each report to the thread pool, so reports could land
    // out of order.
    private sealed class ImmediateProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
