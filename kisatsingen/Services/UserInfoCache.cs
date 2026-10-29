using Microsoft.Extensions.Caching.Memory;

namespace kisatsingen.Services;

public interface IUserInfoCache
{
    Task<List<UserInfo>> GetUsersByObjectIds(Guid[] objectIds, CancellationToken cancellationToken);
}

public sealed class UserInfoCache : IUserInfoCache, IDisposable
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

    public async Task<List<UserInfo>> GetUsersByObjectIds(Guid[] objectIds, CancellationToken cancellationToken)
    {
        List<UserInfo> users = [];
        List<Guid> objectIdsToFetch = [];

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
        var usersFromGraphById = usersFromGraph
            .Where(u => Guid.TryParse(u.Id, out _))
            .ToDictionary(u => Guid.Parse(u.Id!));

        foreach (var objectId in objectIdsToFetch)
        {
            if (!usersFromGraphById.TryGetValue(objectId, out var userFromGraph))
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
                Id = objectId,
                Company = userFromGraph.CompanyName,
                Department = userFromGraph.Department,
                Name = userFromGraph.DisplayName,
                Username = userFromGraph.UserPrincipalName
            };

            _userInfoMemoryCache.Set(GetCacheKey(objectId), userInfo, absoluteExpirationRelativeToNow: _cacheDuration);
            users.Add(userInfo);
        }

        return users;
    }

    public void Dispose()
    {
        _userInfoMemoryCache.Dispose();
    }

    private static string GetCacheKey(Guid objectId) => $"{UserInfoMemoryCacheKey}_{objectId}";
}

public sealed class UserInfo
{
    public required Guid Id { get; init; }
    public string? Company { get; init; }
    public string? Department { get; init; }
    public string? Name { get; init; }
    public string? Username { get; init; }
}
