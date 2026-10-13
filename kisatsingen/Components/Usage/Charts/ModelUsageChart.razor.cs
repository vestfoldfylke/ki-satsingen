using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class ModelUsageChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<string>> Breakdown { get; set; } = [];

    private IReadOnlyList<ModelPoint> _points = [];

    protected override void OnParametersSet()
    {
        _points = Breakdown
            .Select(row => new ModelPoint(
                string.IsNullOrEmpty(row.Key) ? "(ukjent)" : row.Key,
                row.InputTokens,
                row.OutputTokens,
                row.EstimatedInputTokens,
                row.EstimatedOutputTokens))
            .ToList();
    }

    private sealed record ModelPoint(
        string Model,
        long InputTokens,
        long OutputTokens,
        long EstimatedInputTokens,
        long EstimatedOutputTokens);
}
