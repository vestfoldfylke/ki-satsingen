using kisatsingen.Data.Entities;
using kisatsingen.Services;

namespace kisatsingen.Components.Usage;

public interface IUserUsageResolver
{
    /// <summary>
    /// Pairs each usage row with the owner's user info. Names are cosmetic, so a failed lookup
    /// is logged and every row is returned without user info, falling back to the object id.
    /// </summary>
    Task<IReadOnlyList<UserUsage>> ResolveAsync(IReadOnlyList<UsageByKey<Guid>> usagePerOwner, CancellationToken cancellationToken);
}

public class UserUsageResolver : IUserUsageResolver
{
    private readonly IUserInfoCache _userInfoCache;
    private readonly ILogger<UserUsageResolver> _logger;

    public UserUsageResolver(IUserInfoCache userInfoCache, ILogger<UserUsageResolver> logger)
    {
        _userInfoCache = userInfoCache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserUsage>> ResolveAsync(IReadOnlyList<UsageByKey<Guid>> usagePerOwner, CancellationToken cancellationToken)
    {
        Dictionary<Guid, UserInfo> userInfoById = [];

        try
        {
            var userInfos = await _userInfoCache.GetUsersByObjectIds([.. usagePerOwner.Select(u => u.Key)], cancellationToken);
            userInfoById = userInfos.ToDictionary(userInfo => userInfo.Id);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to fill user info for {UserCount} users. Some or all user infos might be missing. Falling back to objectId", usagePerOwner.Count);
        }

        return [.. usagePerOwner.Select(row => new UserUsage(row, userInfoById.GetValueOrDefault(row.Key)))];
    }
}
