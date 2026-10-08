namespace kisatsingen.Data.Entities;

public enum UsageBucket
{
    Hour,
    Day,
    Week,
    Month
}

public static class UsageBucketHelpers
{
    public static string BucketField(UsageBucket bucket) => bucket switch
    {
        UsageBucket.Hour => "hour",
        UsageBucket.Day => "day",
        UsageBucket.Week => "week",
        UsageBucket.Month => "month",
        _ => "day"
    };
}