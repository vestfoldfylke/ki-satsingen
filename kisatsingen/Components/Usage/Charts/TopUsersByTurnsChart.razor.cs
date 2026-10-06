using ApexCharts;
using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class TopUsersByTurnsChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<string>> TopUsers { get; set; } = [];

    private readonly ApexChartOptions<UsageByKey<string>> _options = new()
    {
        Chart = new Chart
        {
            Height = 320,
            Toolbar = new Toolbar { Show = false }
        },
        PlotOptions = new PlotOptions
        {
            Bar = new PlotOptionsBar { Horizontal = true }
        },
        DataLabels = new DataLabels { Enabled = true },
        // Entra object identifiers are 36-char GUIDs. The ApexCharts default of
        // 160px truncates them with ellipsis; widen the y-axis label slot so the
        // whole id is readable.
        Yaxis =
        [
            new YAxis
            {
                Labels = new YAxisLabels { MaxWidth = 400 }
            }
        ]
    };
}
