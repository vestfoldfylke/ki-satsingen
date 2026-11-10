using kisatsingen.Data.Entities;
using kisatsingen.Services;

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
    public static DateTimeOffset? Since(this UsageRange range)
    {
        var today = DateTimeService.GetTimeZonedDateTimeOffset(DateTimeOffset.UtcNow).Date;

        DateTime? start = range switch
        {
            UsageRange.Last7Days => today.AddDays(-6),
            UsageRange.Last30Days => today.AddDays(-29),
            UsageRange.Last90Days => today.AddDays(-89),
            _ => null
        };

        return start is not null
            ? (DateTimeOffset)new DateTimeOffset(start.Value).UtcDateTime
            : null;
    }

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
