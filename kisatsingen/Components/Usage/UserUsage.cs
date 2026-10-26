using kisatsingen.Data.Entities;

namespace kisatsingen.Components.Usage;

public sealed record UserUsage(UsageByKey<Guid> Usage, UserInfo? UserInfo)
{
    public string DisplayName => UserInfo?.Name ?? Usage.Key.ToString();
}
