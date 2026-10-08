using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class ProviderUsageChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<string>> Breakdown { get; set; } = [];

    private IReadOnlyList<ProviderPoint> _points = [];

    protected override void OnParametersSet()
    {
        _points = Breakdown
            .Select(row => new ProviderPoint(
                string.IsNullOrEmpty(row.Key) ? "(ukjent)" : row.Key,
                row.InputTokens,
                row.OutputTokens,
                row.EstimatedInputTokens,
                row.EstimatedOutputTokens))
            .ToList();
    }

    private sealed record ProviderPoint(
        string Provider,
        long InputTokens,
        long OutputTokens,
        long EstimatedInputTokens,
        long EstimatedOutputTokens);
}
