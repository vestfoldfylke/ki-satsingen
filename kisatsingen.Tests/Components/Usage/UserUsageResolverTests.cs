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

    private static readonly Guid AdaId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid BobId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeletedUserId = Guid.Parse("e3b0c442-98fc-4c14-9afb-f4c8996fb924");

    public UserUsageResolverTests()
    {
        _cache = new UserInfoCache(_graph);
        _resolver = new UserUsageResolver(_cache, NullLogger<UserUsageResolver>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    private static UsageByKey<Guid> UsageFor(Guid ownerId, long inputTokens = 0) => new(ownerId, inputTokens, 0, 0, 0, 1);

    [Fact]
    public async Task Each_row_is_paired_with_its_owners_user_info()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser(BobId, "Bob"));

        var rows = await _resolver.ResolveAsync([UsageFor(AdaId), UsageFor(BobId)], CancellationToken.None);

        Assert.Equal(["Ada", "Bob"], rows.Select(row => row.DisplayName));
    }

    [Fact]
    public async Task Rows_keep_their_order_and_usage_numbers()
    {
        _graph.Users.Add(FakeGraphService.CreateUser(AdaId, "Ada"));
        _graph.Users.Add(FakeGraphService.CreateUser(BobId, "Bob"));
        UsageByKey<Guid>[] usage = [UsageFor(BobId, inputTokens: 200), UsageFor(AdaId, inputTokens: 100)];

        var rows = await _resolver.ResolveAsync(usage, CancellationToken.None);

        Assert.Equal(usage, rows.Select(row => row.Usage));
    }

    [Fact]
    public async Task A_row_whose_owner_is_not_in_graph_shows_the_object_id()
    {
        var rows = await _resolver.ResolveAsync([UsageFor(DeletedUserId)], CancellationToken.None);

        Assert.Equal(DeletedUserId.ToString(), Assert.Single(rows).DisplayName);
    }

    // Names are cosmetic: a Graph failure must not take the usage tables down with it.
    [Fact]
    public async Task Every_row_is_still_returned_with_its_object_id_when_graph_fails()
    {
        _graph.FailWith = new ODataError { ResponseStatusCode = 429 };

        var rows = await _resolver.ResolveAsync([UsageFor(AdaId), UsageFor(BobId)], CancellationToken.None);

        Assert.Equal([AdaId.ToString(), BobId.ToString()], rows.Select(row => row.DisplayName));
        Assert.All(rows, row => Assert.Null(row.UserInfo));
    }

    // HttpClient reports a timeout as a cancellation the caller never asked for.
    [Fact]
    public async Task Every_row_is_still_returned_with_its_object_id_when_graph_times_out()
    {
        _graph.FailWith = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");

        var rows = await _resolver.ResolveAsync([UsageFor(AdaId), UsageFor(BobId)], CancellationToken.None);

        Assert.Equal([AdaId.ToString(), BobId.ToString()], rows.Select(row => row.DisplayName));
        Assert.All(rows, row => Assert.Null(row.UserInfo));
    }

    // A newer reload took over, so the page must stop rather than show stale rows.
    [Fact]
    public async Task Cancellation_by_the_caller_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _graph.FailWith = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _resolver.ResolveAsync([UsageFor(AdaId)], cancellation.Token));
    }
}
