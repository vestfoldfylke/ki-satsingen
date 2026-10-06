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
    public required ITokenUsageRepository TokenUsageRepository { get; set; }

    [Inject]
    public required ILogger<MyUsage> Logger { get; set; }

    private UsageRange Range { get; set; } = UsageRange.All;
    private bool IsLoading { get; set; }
    private string? LoadError { get; set; }
    private CancellationTokenSource? _reloadCts;

    private string? _ownerId;

    private TokenUsageSummary Totals { get; set; } = new(0, 0, 0, 0, 0);
    private IReadOnlyList<UsageTimeBucket> TimeSeries { get; set; } = [];
    private IReadOnlyList<UsageByKey<TurnStatus>> StatusBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ProviderBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ModelBreakdown { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        _ownerId = await AuthenticationService.GetUserObjectIdentifierAsync();
        if (string.IsNullOrWhiteSpace(_ownerId))
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
        if (string.IsNullOrWhiteSpace(_ownerId))
        {
            return;
        }

        _reloadCts?.Cancel();
        _reloadCts = new CancellationTokenSource();
        var ct = _reloadCts.Token;

        IsLoading = true;
        LoadError = null;

        try
        {
            var since = Range.Since();
            var bucket = Range.Bucket();

            var totalsTask = TokenUsageRepository.GetTotalsAsync(_ownerId, since, ct);
            var timeSeriesTask = TokenUsageRepository.GetTimeSeriesAsync(_ownerId, since, bucket, ct);
            var statusTask = TokenUsageRepository.GetUsageByStatusAsync(_ownerId, since, ct);
            var providerTask = TokenUsageRepository.GetUsageByProviderAsync(_ownerId, since, ct);
            var modelTask = TokenUsageRepository.GetUsageByModelAsync(_ownerId, since, ct);

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
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
    }
}
