using kisatsingen.Data.Entities;

namespace kisatsingen.Components.Usage;

public enum UsageRange
{
    Last7Days,
    Last30Days,
    Last90Days,
    All
}

public static class UsageRangeExtensions
{
    public static DateTimeOffset? Since(this UsageRange range) => range switch
    {
        UsageRange.Last7Days => DateTimeOffset.UtcNow.AddDays(-7),
        UsageRange.Last30Days => DateTimeOffset.UtcNow.AddDays(-30),
        UsageRange.Last90Days => DateTimeOffset.UtcNow.AddDays(-90),
        UsageRange.All => null,
        _ => null
    };

    public static UsageBucket Bucket(this UsageRange range) => range switch
    {
        UsageRange.Last7Days => UsageBucket.Day,
        UsageRange.Last30Days => UsageBucket.Day,
        UsageRange.Last90Days => UsageBucket.Week,
        UsageRange.All => UsageBucket.Month,
        _ => UsageBucket.Day
    };

    public static string Label(this UsageRange range) => range switch
    {
        UsageRange.Last7Days => "7 dager",
        UsageRange.Last30Days => "30 dager",
        UsageRange.Last90Days => "90 dager",
        UsageRange.All => "Alt",
        _ => "30 dager"
    };
}
