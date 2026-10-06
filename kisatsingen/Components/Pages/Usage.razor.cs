using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

[Authorize(Policy = "IsAdministrator")]
public partial class Usage : ComponentBase, IDisposable
{
    [Inject]
    public required IChatModelCatalog ChatModelCatalog { get; init; }

    [Inject]
    public required ITokenUsageRepository TokenUsageRepository { get; init; }

    [Inject]
    public required ILogger<Usage> Logger { get; init; }

    private const int TopUserCount = 5;

    private UsageRange Range { get; set; } = UsageRange.Last7Days;
    private bool IsLoading { get; set; }
    private string? LoadError { get; set; }
    private CancellationTokenSource? _reloadCts;

    private TokenUsageSummary Totals { get; set; } = new(0, 0, 0, 0, 0);
    private IReadOnlyList<UsageTimeBucket> TimeSeries { get; set; } = [];
    private IReadOnlyList<UsageByKey<TurnStatus>> StatusBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ProviderBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> ModelBreakdown { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> TopUsersByTurns { get; set; } = [];
    private IReadOnlyList<UsageByKey<string>> UsagePerUser { get; set; } = [];

    private IReadOnlyList<ChatModel> ChatModels => ChatModelCatalog.Models;

    private IEnumerable<UsageByKey<string>> TopTokenUsageByInputTokens =>
        UsagePerUser.OrderByDescending(u => u.InputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByOutputTokens =>
        UsagePerUser.OrderByDescending(u => u.OutputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByEstimatedInputTokens =>
        UsagePerUser.OrderByDescending(u => u.EstimatedInputTokens).Take(TopUserCount);

    private IEnumerable<UsageByKey<string>> TopTokenUsageByEstimatedOutputTokens =>
        UsagePerUser.OrderByDescending(u => u.EstimatedOutputTokens).Take(TopUserCount);

    protected override Task OnInitializedAsync() => ReloadAsync();

    private Task OnRangeChanged(UsageRange range)
    {
        Range = range;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _reloadCts?.Cancel();
        _reloadCts = new CancellationTokenSource();
        var ct = _reloadCts.Token;

        IsLoading = true;
        LoadError = null;

        try
        {
            var since = Range.Since();
            var bucket = Range.Bucket();

            var totalsTask = TokenUsageRepository.GetTotalsAsync(since, ct);
            var timeSeriesTask = TokenUsageRepository.GetTimeSeriesAsync(since, bucket, ct);
            var statusTask = TokenUsageRepository.GetUsageByStatusAsync(since, ct);
            var providerTask = TokenUsageRepository.GetUsageByProviderAsync(since, ct);
            var modelTask = TokenUsageRepository.GetUsageByModelAsync(since, ct);
            var topUsersTask = TokenUsageRepository.GetTopUsersByTurnsAsync(since, TopUserCount, ct);
            var perUserTask = TokenUsageRepository.GetUsagePerUserAsync(since, ct);

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
