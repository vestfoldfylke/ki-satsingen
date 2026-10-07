using kisatsingen.Data.Entities;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class TurnStatusChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<TurnStatus>> Breakdown { get; set; } = [];

    private IReadOnlyList<StatusPoint> _points = [];

    protected override void OnParametersSet()
    {
        _points = Breakdown
            .Select(row => new StatusPoint(row.Key.ToString(), row.TurnCount))
            .ToList();
    }

    private sealed record StatusPoint(string Status, int TurnCount);
}
