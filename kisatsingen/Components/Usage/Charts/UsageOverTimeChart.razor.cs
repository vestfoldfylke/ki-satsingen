using ApexCharts;
using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class UsageOverTimeChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageTimeBucket> Series { get; set; } = [];

    [Parameter]
    public UsageBucket Bucket { get; set; } = UsageBucket.Day;

    private ApexChartOptions<UsageTimeBucket> _options = null!;

    protected override void OnParametersSet()
    {
        _options = new ApexChartOptions<UsageTimeBucket>
        {
            Chart = new Chart
            {
                Height = 320,
                Stacked = false,
                Toolbar = new Toolbar { Show = false },
                Zoom = new Zoom { Enabled = false }
            },
            Stroke = new Stroke { Curve = Curve.Smooth, Width = 2 },
            DataLabels = new DataLabels { Enabled = false },
            Fill = new Fill { Opacity = [0.35, 0.35, 0.15, 0.15] },
            // No JS-string Formatter anywhere in the options tree: Blazor-ApexCharts
            // uses eval() to turn such strings into functions, which the app's CSP
            // (script-src 'self') correctly blocks. Axis numbers render unformatted
            // as a result — localisation would need an interop that avoids eval.
            Xaxis = new XAxis
            {
                Type = XAxisType.Datetime,
                Labels = new XAxisLabels
                {
                    DatetimeUTC = false,
                    Format = Bucket switch
                    {
                        UsageBucket.Hour => "HH:mm",
                        UsageBucket.Month => "MMM yyyy",
                        _ => "dd. MMM"
                    }
                }
            },
            Legend = new Legend { Position = LegendPosition.Bottom }
        };
    }
}
