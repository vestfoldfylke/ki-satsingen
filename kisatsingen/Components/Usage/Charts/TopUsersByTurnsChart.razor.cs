using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Usage.Charts;

public partial class TopUsersByTurnsChart : ComponentBase
{
    [Parameter]
    public IReadOnlyList<UsageByKey<string>> TopUsers { get; set; } = [];

    private IReadOnlyList<UserTurnPoint> _points = [];

    protected override void OnParametersSet()
    {
        _points = TopUsers
            .Select(row => new UserTurnPoint(row.Key, row.TurnCount))
            .ToList();
    }

    private sealed record UserTurnPoint(string OwnerId, int TurnCount);
}
