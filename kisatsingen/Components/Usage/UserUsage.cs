using kisatsingen.Data.Entities;
using kisatsingen.Services;

namespace kisatsingen.Components.Usage;

public sealed record UserUsage(UsageByKey<Guid> Usage, UserInfo? UserInfo)
{
    public string DisplayName => UserInfo?.Name ?? Usage.Key.ToString();
}
