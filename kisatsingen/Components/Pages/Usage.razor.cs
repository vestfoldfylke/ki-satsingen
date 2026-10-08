using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public partial class Usage : ComponentBase, IDisposable
{
    [Inject]
    public required IChatModelCatalog ChatModelCatalog { get; init; }

    [Inject]
    public required IOrganizationTokenUsageRepository OrganizationTokenUsageRepository { get; init; }

    [Inject]
    public required ILogger<Usage> Logger { get; init; }

    private const int TopUserCount = 5;

    private UsageRange Range { get; set; } = UsageRange.Last7Days;
    private string? LoadError { get; set; }
    private CancellationTokenSource? _reloadCts;

    private TokenUsageSummary Totals { get; set; } = new(0, 0, 0, 0, 0);
    private IReadOnlyList<UsageTimeBucket> TimeSeries { get; set; } = [];
    private IReadOnlyList<UsageByKey<TurnStatus>> StatusBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ProviderBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ModelBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> TopUsersByTurns { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> UsagePerUser { get; set; } = [];


    private IEnumerable<UsageByKey<string>> TopTokenUsageByInputTokens =>
        UsagePerUser.OrderByDescending(u => u.InputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByOutputTokens =>
        UsagePerUser.OrderByDescending(u => u.OutputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByEstimatedInputTokens =>
        UsagePerUser.OrderByDescending(u => u.EstimatedInputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByEstimatedOutputTokens =>
        UsagePerUser.OrderByDescending(u => u.EstimatedOutputTokens).Take(TopUserCount);

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task OnRangeChanged(UsageRange range)
    {
        var previousRange = Range;

        Range = range;
        await ReloadAsync();

        if (!string.IsNullOrWhiteSpace(LoadError))
        {
            Range = previousRange;
        }
    }

    private async Task ReloadAsync()
    {
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

            var totalsTask = OrganizationTokenUsageRepository.GetTotalsAsync(since, ct);
            var timeSeriesTask = OrganizationTokenUsageRepository.GetTimeSeriesAsync(since, bucket, ct);
            var statusTask = OrganizationTokenUsageRepository.GetUsageByStatusAsync(since, ct);
            var providerTask = OrganizationTokenUsageRepository.GetUsageByProviderAsync(since, ct);
            var modelTask = OrganizationTokenUsageRepository.GetUsageByModelAsync(since, ct);
            var topUsersTask = OrganizationTokenUsageRepository.GetTopUsersByTurnsAsync(since, TopUserCount, ct);
            var perUserTask = OrganizationTokenUsageRepository.GetUsagePerUserAsync(since, ct);

            await Task.WhenAll(
                totalsTask, timeSeriesTask, statusTask, providerTask,
                modelTask, topUsersTask, perUserTask);

            Totals = await totalsTask;
            TimeSeries = await timeSeriesTask;
            StatusBreakdown = await statusTask;
            ProviderBreakdown = await providerTask;
            ModelBreakdown = await modelTask;
            TopUsersByTurns = await topUsersTask;
            UsagePerUser = await perUserTask;
        }
        catch (OperationCanceledException)
        {
            // A newer reload took over.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reload organization token usage for range {Range}", Range);
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
