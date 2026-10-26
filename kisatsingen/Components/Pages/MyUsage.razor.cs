using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public partial class MyUsage : ComponentBase, IDisposable
{
    [Inject]
    public required IAuthenticationService AuthenticationService { private get; set; }

    [Inject]
    public required IUserTokenUsageRepository UserTokenUsageRepository { get; set; }

    [Inject]
    public required ILogger<MyUsage> Logger { get; set; }

    private UsageRange Range { get; set; } = UsageRange.Last7Days;
    private string? LoadError { get; set; }
    private CancellationTokenSource? _reloadCts;

    private Guid? _ownerId;

    private TokenUsageSummary Totals { get; set; } = new(0, 0, 0, 0, 0);
    private IReadOnlyList<UsageTimeBucket> TimeSeries { get; set; } = [];
    private IReadOnlyList<UsageByKey<TurnStatus>> StatusBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ProviderBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ModelBreakdown { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        _ownerId = await AuthenticationService.GetUserObjectIdentifierAsync();
        if (_ownerId is null)
        {
            throw new UnauthorizedAccessException();
        }

        await ReloadAsync();
    }

    private Task OnRangeChanged(UsageRange range)
    {
        Range = range;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_ownerId is not { } ownerId)
        {
            return;
        }

        if (_reloadCts is not null)
        {
            await _reloadCts.CancelAsync();
            _reloadCts.Dispose();
        }

        _reloadCts = new CancellationTokenSource();
        var ct = _reloadCts.Token;

        LoadError = null;

        try
        {
            var since = Range.Since();
            var bucket = Range.Bucket();

            var totalsTask = UserTokenUsageRepository.GetTotalsAsync(ownerId, since, ct);
            var timeSeriesTask = UserTokenUsageRepository.GetTimeSeriesAsync(ownerId, since, bucket, ct);
            var statusTask = UserTokenUsageRepository.GetUsageByStatusAsync(ownerId, since, ct);
            var providerTask = UserTokenUsageRepository.GetUsageByProviderAsync(ownerId, since, ct);
            var modelTask = UserTokenUsageRepository.GetUsageByModelAsync(ownerId, since, ct);

            await Task.WhenAll(totalsTask, timeSeriesTask, statusTask, providerTask, modelTask);

            Totals = await totalsTask;
            TimeSeries = await timeSeriesTask;
            StatusBreakdown = await statusTask;
            ProviderBreakdown = await providerTask;
            ModelBreakdown = await modelTask;
        }
        catch (OperationCanceledException)
        {
            // A newer reload took over.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reload personal token usage for range {Range}", Range);
            LoadError = "Kunne ikke laste forbruk.";
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
    }
}
