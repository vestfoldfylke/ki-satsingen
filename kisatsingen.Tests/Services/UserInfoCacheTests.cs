using kisatsingen.Services;
using Xunit;

namespace kisatsingen.Tests.Services;

public sealed class UserInfoCacheTests : IDisposable
{
    private readonly FakeGraphService _graph = new();
    private readonly UserInfoCache _cache;

    private static readonly Guid AdaId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid BobId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeletedUserId = Guid.Parse("e3b0c442-98fc-4c14-9afb-f4c8996fb924");

    public UserInfoCacheTests() => _cache = new UserInfoCache(_graph);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task A_user_found_in_graph_is_returned_with_its_name()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));

        var users = await _cache.GetUsersByObjectIds([AdaId], CancellationToken.None);

        Assert.Equal("Ada", Assert.Single(users).Name);
    }

    [Fact]
    public async Task A_cached_user_is_returned_without_calling_graph_again()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));
        await _cache.GetUsersByObjectIds([AdaId], CancellationToken.None);

        var users = await _cache.GetUsersByObjectIds([AdaId], CancellationToken.None);

        Assert.Single(_graph.Calls);
        Assert.Equal("Ada", Assert.Single(users).Name);
    }

    [Fact]
    public async Task Only_uncached_ids_are_sent_to_graph()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser(BobId, "Bob"));
        await _cache.GetUsersByObjectIds([AdaId], CancellationToken.None);

        await _cache.GetUsersByObjectIds([AdaId, BobId], CancellationToken.None);

        Assert.Equal([BobId], _graph.Calls[^1]);
    }

    [Fact]
    public async Task A_user_graph_does_not_return_is_cached_as_not_found()
    {
        await _cache.GetUsersByObjectIds([DeletedUserId], CancellationToken.None);

        await _cache.GetUsersByObjectIds([DeletedUserId], CancellationToken.None);

        Assert.Single(_graph.Calls);
    }

    // The page shows the object id when Name is null, so a not-found user must not get one.
    [Fact]
    public async Task A_not_found_user_is_returned_with_its_id_and_no_name()
    {
        var users = await _cache.GetUsersByObjectIds([DeletedUserId], CancellationToken.None);

        var user = Assert.Single(users);
        Assert.Equal(DeletedUserId, user.Id);
        Assert.Null(user.Name);
    }

    [Fact]
    public async Task Duplicate_ids_are_sent_to_graph_once_and_returned_once()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));

        var users = await _cache.GetUsersByObjectIds([AdaId, AdaId], CancellationToken.None);

        Assert.Equal([AdaId], Assert.Single(_graph.Calls));
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
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));
        _graph.FailWith = new HttpRequestException("Graph is down");
        await Assert.ThrowsAsync<HttpRequestException>(() => _cache.GetUsersByObjectIds([AdaId], CancellationToken.None));
        _graph.FailWith = null;

        var users = await _cache.GetUsersByObjectIds([AdaId], CancellationToken.None);

        Assert.Equal("Ada", Assert.Single(users).Name);
    }
}
