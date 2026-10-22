using kisatsingen.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace kisatsingen.Services;

public interface IUserInfoCache
{
    Task<List<UserInfo>> GetUsersByObjectIds(string[] objectIds, CancellationToken cancellationToken);
}

public class UserInfoCache : IUserInfoCache, IDisposable
{
    private readonly IMemoryCache _userInfoMemoryCache;
    private readonly IGraphService _graphService;

    private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(6);
    private readonly TimeSpan _notFoundCacheDuration = TimeSpan.FromMinutes(30);

    private const string UserInfoMemoryCacheKey = "UserInfoCache";

    public UserInfoCache(IGraphService graphService)
    {
        _userInfoMemoryCache = new MemoryCache(new MemoryCacheOptions());
        _graphService = graphService;
    }

    public async Task<List<UserInfo>> GetUsersByObjectIds(string[] objectIds, CancellationToken cancellationToken)
    {
        List<UserInfo> users = [];
        List<string> objectIdsToFetch = [];

        foreach (var objectId in objectIds.Distinct())
        {
            if (_userInfoMemoryCache.TryGetValue<UserInfo>(GetCacheKey(objectId), out var userInfo) &&
                userInfo is not null)
            {
                users.Add(userInfo);
                continue;
            }

            objectIdsToFetch.Add(objectId);
        }

        if (objectIdsToFetch.Count == 0)
        {
            return users;
        }

        var usersFromGraph = await _graphService.GetUsersByObjectIds([.. objectIdsToFetch], cancellationToken);
        foreach (var objectId in objectIdsToFetch)
        {
            var userFromGraph = usersFromGraph.FirstOrDefault(u => u.Id == objectId);
            if (userFromGraph?.Id is null)
            {
                var notFoundUserInfo = new UserInfo
                {
                    Id = objectId
                };

                _userInfoMemoryCache.Set(GetCacheKey(objectId), notFoundUserInfo, absoluteExpirationRelativeToNow: _notFoundCacheDuration);
                users.Add(notFoundUserInfo);
                continue;
            }

            var userInfo = new UserInfo
            {
                Id = userFromGraph.Id,
                Company = userFromGraph.CompanyName,
                Department = userFromGraph.Department,
                Name = userFromGraph.DisplayName,
                Username = userFromGraph.UserPrincipalName
            };

            _userInfoMemoryCache.Set(GetCacheKey(userFromGraph.Id), userInfo, absoluteExpirationRelativeToNow: _cacheDuration);
            users.Add(userInfo);
        }

        return users;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _userInfoMemoryCache.Dispose();
    }

    private string GetCacheKey(string objectId) => $"{UserInfoMemoryCacheKey}_{objectId}";
}