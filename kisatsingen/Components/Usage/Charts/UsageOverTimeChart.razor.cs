using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class UsageOverTimeChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageTimeBucket> Series { get; set; } = [];

    [Parameter]
    public UsageBucket Bucket { get; set; } = UsageBucket.Day;

    private IReadOnlyList<TimePoint> _points = [];
    private string _dateFormat = "{0:dd. MMM}";

    protected override void OnParametersSet()
    {
        _points = Series
            .Select(row => new TimePoint(
                row.Bucket.LocalDateTime,
                row.InputTokens,
                row.OutputTokens,
                row.EstimatedInputTokens,
                row.EstimatedOutputTokens))
            .ToList();

        _dateFormat = Bucket switch
        {
            UsageBucket.Hour => "{0:HH:mm}",
            UsageBucket.Month => "{0:MMM yyyy}",
            _ => "{0:dd. MMM}"
        };
    }

    private sealed record TimePoint(
        DateTime Bucket,
        long InputTokens,
        long OutputTokens,
        long EstimatedInputTokens,
        long EstimatedOutputTokens);
}
