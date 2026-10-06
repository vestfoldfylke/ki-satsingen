using ApexCharts;
using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class TurnStatusChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<TurnStatus>> Breakdown { get; set; } = [];

    private readonly ApexChartOptions<UsageByKey<TurnStatus>> _options = new()
    {
        Chart = new Chart { Height = 320 },
        Legend = new Legend { Position = LegendPosition.Bottom },
        DataLabels = new DataLabels { Enabled = true }
    };
}
