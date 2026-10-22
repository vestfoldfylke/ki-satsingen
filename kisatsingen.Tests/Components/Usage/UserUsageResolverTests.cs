using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using kisatsingen.Services;
using kisatsingen.Tests.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Xunit;

namespace kisatsingen.Tests.Components.Usage;

public sealed class UserUsageResolverTests : IDisposable
{
    private readonly FakeGraphService _graph = new();
    private readonly UserInfoCache _cache;
    private readonly UserUsageResolver _resolver;

    public UserUsageResolverTests()
    {
        _cache = new UserInfoCache(_graph);
        _resolver = new UserUsageResolver(_cache, NullLogger<UserUsageResolver>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    private static UsageByKey<string> UsageFor(string ownerId, long inputTokens = 0) => new(ownerId, inputTokens, 0, 0, 0, 1);

    [Fact]
    public async Task Each_row_is_paired_with_its_owners_user_info()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser("b", "Bob"));

        var rows = await _resolver.ResolveAsync([UsageFor("a"), UsageFor("b")], CancellationToken.None);

        Assert.Equal(["Ada", "Bob"], rows.Select(row => row.DisplayName));
    }

    [Fact]
    public async Task Rows_keep_their_order_and_usage_numbers()
    {
        _graph.Users.Add(FakeGraphService.CreateUser("a", "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser("b", "Bob"));
        UsageByKey<string>[] usage = [UsageFor("b", inputTokens: 200), UsageFor("a", inputTokens: 100)];

        var rows = await _resolver.ResolveAsync(usage, CancellationToken.None);

        Assert.Equal(usage, rows.Select(row => row.Usage));
    }

    [Fact]
    public async Task A_row_whose_owner_is_not_in_graph_shows_the_object_id()
    {
        var rows = await _resolver.ResolveAsync([UsageFor("deleted")], CancellationToken.None);

        Assert.Equal("deleted", Assert.Single(rows).DisplayName);
    }

    // Names are cosmetic: a Graph failure must not take the usage tables down with it.
    [Fact]
    public async Task Every_row_is_still_returned_with_its_object_id_when_graph_fails()
    {
        _graph.FailWith = new ODataError { ResponseStatusCode = 429 };

        var rows = await _resolver.ResolveAsync([UsageFor("a"), UsageFor("b")], CancellationToken.None);

        Assert.Equal(["a", "b"], rows.Select(row => row.DisplayName));
        Assert.All(rows, row => Assert.Null(row.UserInfo));
    }

    // A newer reload took over, so the page must stop rather than show stale rows.
    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        _graph.FailWith = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _resolver.ResolveAsync([UsageFor("a")], CancellationToken.None));
    }
}
