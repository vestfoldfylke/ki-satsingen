using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public partial class Usage : ComponentBase, IDisposable
{
    [Inject]
    public required IOrganizationTokenUsageRepository OrganizationTokenUsageRepository { get; init; }

    [Inject]
    public required ILogger<Usage> Logger { get; init; }

    [Inject]
    public required IUserUsageResolver UserUsageResolver { get; init; }

    private const int TopUserCount = 5;

    private UsageRange Range { get; set; } = UsageRange.Last7Days;
    private string? LoadError { get; set; }
    private CancellationTokenSource? _reloadCts;

    private TokenUsageSummary Totals { get; set; } = new(0, 0, 0, 0, 0);
    private IReadOnlyList<UsageTimeBucket> TimeSeries { get; set; } = [];
    private IReadOnlyList<UsageByKey<TurnStatus>> StatusBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ProviderBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ModelBreakdown { get; set; } = [];
    private IReadOnlyList<UserUsage> TopUsersByTurns { get; set; } = [];

    private IEnumerable<UserUsage> TopTokenUsageByInputTokens { get; set; } = [];
    private IEnumerable<UserUsage> TopTokenUsageByOutputTokens { get; set; } = [];
    private IEnumerable<UserUsage> TopTokenUsageByEstimatedInputTokens { get; set; } = [];
    private IEnumerable<UserUsage> TopTokenUsageByEstimatedOutputTokens { get; set; } = [];

    protected override Task OnInitializedAsync() => ReloadAsync();

    private Task OnRangeChanged(UsageRange range)
    {
        Range = range;
        return ReloadAsync();
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
            var topTokenUsageByInputTokensTask = OrganizationTokenUsageRepository.GetTopUsersByTokenUsageAsync(since, TopUsersByTokenUsageType.InputTokens, TopUserCount, ct);
            var topTokenUsageByOutputTokensTask = OrganizationTokenUsageRepository.GetTopUsersByTokenUsageAsync(since, TopUsersByTokenUsageType.OutputTokens, TopUserCount, ct);
            var topTokenUsageByEstimatedInputTokensTask = OrganizationTokenUsageRepository.GetTopUsersByTokenUsageAsync(since, TopUsersByTokenUsageType.EstimatedInputTokens, TopUserCount, ct);
            var topTokenUsageByEstimatedOutputTokensTask = OrganizationTokenUsageRepository.GetTopUsersByTokenUsageAsync(since, TopUsersByTokenUsageType.EstimatedOutputTokens, TopUserCount, ct);

            await Task.WhenAll(
                totalsTask, timeSeriesTask, statusTask, providerTask,
                modelTask, topUsersTask, topTokenUsageByInputTokensTask,
                topTokenUsageByOutputTokensTask, topTokenUsageByEstimatedInputTokensTask,
                topTokenUsageByEstimatedOutputTokensTask);

            Totals = await totalsTask;
            TimeSeries = await timeSeriesTask;
            StatusBreakdown = await statusTask;
            ProviderBreakdown = await providerTask;
            ModelBreakdown = await modelTask;

            TopUsersByTurns = await UserUsageResolver.ResolveAsync(await topUsersTask, ct);
            TopTokenUsageByInputTokens = await UserUsageResolver.ResolveAsync(await topTokenUsageByInputTokensTask, ct);
            TopTokenUsageByOutputTokens = await UserUsageResolver.ResolveAsync(await topTokenUsageByOutputTokensTask, ct);
            TopTokenUsageByEstimatedInputTokens = await UserUsageResolver.ResolveAsync(await topTokenUsageByEstimatedInputTokensTask, ct);
            TopTokenUsageByEstimatedOutputTokens = await UserUsageResolver.ResolveAsync(await topTokenUsageByEstimatedOutputTokensTask, ct);
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
