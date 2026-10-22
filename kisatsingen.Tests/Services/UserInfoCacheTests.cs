using kisatsingen.Services;
using Xunit;

namespace kisatsingen.Tests.Services;

public sealed class UserInfoCacheTests : IDisposable
{
    private readonly FakeGraphService _graph = new();
    private readonly UserInfoCache _cache;

    public UserInfoCacheTests() => _cache = new UserInfoCache(_graph);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task A_user_found_in_graph_is_returned_with_its_name()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));

        var users = await _cache.GetUsersByObjectIds(["a"], CancellationToken.None);

        Assert.Equal("Ada", Assert.Single(users).Name);
    }

    [Fact]
    public async Task A_cached_user_is_returned_without_calling_graph_again()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));
        await _cache.GetUsersByObjectIds(["a"], CancellationToken.None);

        var users = await _cache.GetUsersByObjectIds(["a"], CancellationToken.None);

        Assert.Single(_graph.Calls);
        Assert.Equal("Ada", Assert.Single(users).Name);
    }

    [Fact]
    public async Task Only_uncached_ids_are_sent_to_graph()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser("b", "Bob"));
        await _cache.GetUsersByObjectIds(["a"], CancellationToken.None);

        await _cache.GetUsersByObjectIds(["a", "b"], CancellationToken.None);

        Assert.Equal(["b"], _graph.Calls[^1]);
    }

    [Fact]
    public async Task A_user_graph_does_not_return_is_cached_as_not_found()
    {
        await _cache.GetUsersByObjectIds(["deleted"], CancellationToken.None);

        await _cache.GetUsersByObjectIds(["deleted"], CancellationToken.None);

        Assert.Single(_graph.Calls);
    }

    // The page shows the object id when Name is null, so a not-found user must not get one.
    [Fact]
    public async Task A_not_found_user_is_returned_with_its_id_and_no_name()
    {
        var users = await _cache.GetUsersByObjectIds(["deleted"], CancellationToken.None);

        var user = Assert.Single(users);
        Assert.Equal("deleted", user.Id);
        Assert.Null(user.Name);
    }

    [Fact]
    public async Task Duplicate_ids_are_sent_to_graph_once_and_returned_once()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));

        var users = await _cache.GetUsersByObjectIds(["a", "a"], CancellationToken.None);

        Assert.Equal(["a"], Assert.Single(_graph.Calls));
        Assert.Single(users);
    }

    [Fact]
    public async Task An_empty_list_of_ids_does_not_call_graph()
    {
        var users = await _cache.GetUsersByObjectIds([], CancellationToken.None);

        Assert.Empty(_graph.Calls);
        Assert.Empty(users);
    }

    // A failed lookup must not be remembered as "not found", or names stay missing for 30 minutes.
    [Fact]
    public async Task A_failed_graph_call_caches_nothing_so_the_next_call_tries_again()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));
        _graph.FailWith = new HttpRequestException("Graph is down");
        await Assert.ThrowsAsync<HttpRequestException>(() => _cache.GetUsersByObjectIds(["a"], CancellationToken.None));
        _graph.FailWith = null;

        var users = await _cache.GetUsersByObjectIds(["a"], CancellationToken.None);

        Assert.Equal("Ada", Assert.Single(users).Name);
    }
}
