using ApexCharts;
using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class ProviderUsageChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<string>> Breakdown { get; set; } = [];

    private static object ProviderLabel(UsageByKey<string> row) =>
        string.IsNullOrEmpty(row.Key) ? "(ukjent)" : row.Key;

    private readonly ApexChartOptions<UsageByKey<string>> _options = new()
    {
        Chart = new Chart
        {
            Height = 320,
            Stacked = false,
            Toolbar = new Toolbar { Show = false }
        },
        DataLabels = new DataLabels { Enabled = false },
        // No JS-string Formatter: see UsageOverTimeChart for the CSP rationale.
        Legend = new Legend { Position = LegendPosition.Bottom }
    };
}
