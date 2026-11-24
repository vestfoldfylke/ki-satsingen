using kisatsingen.Services.GeneratedFiles;
using Xunit;

namespace kisatsingen.Tests.Services.GeneratedFiles;

public sealed class FileBuildLimiterTests
{
    private static readonly byte[] Content = [1, 2, 3];

    private static ArraySegment<byte> BuildContent() => Content;

    private static FileBuildLimiter Limiter(int maxConcurrentBuilds, TimeSpan maxWait) =>
        new(new FileBuildLimiterOptions(maxConcurrentBuilds, maxWait));

    [Fact]
    public async Task A_build_with_a_free_slot_gives_the_file()
    {
        var limiter = Limiter(1, TimeSpan.FromSeconds(30));

        var result = await limiter.BuildAsync(BuildContent);

        Assert.Equal(Content, Assert.IsType<FileBuildResult.Built>(result).Content.ToArray());
    }

    // The first build holds the only slot until it is released, so the
    // second finds no slot free.
    [Fact]
    public async Task A_build_that_finds_every_slot_taken_for_the_whole_wait_is_turned_away()
    {
        var limiter = Limiter(1, TimeSpan.Zero);
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();

        var first = limiter.BuildAsync(() =>
        {
            started.Set();
            release.Wait();
            return Content;
        });
        started.Wait();
        var second = await limiter.BuildAsync(BuildContent);
        release.Set();
        await first;

        Assert.IsType<FileBuildResult.Busy>(second);
    }

    [Fact]
    public async Task A_build_waiting_for_a_slot_gets_it_when_the_one_before_finishes()
    {
        var limiter = Limiter(1, TimeSpan.FromSeconds(30));

        var results = await Task.WhenAll(
            limiter.BuildAsync(BuildContent),
            limiter.BuildAsync(BuildContent));

        Assert.All(results, result => Assert.IsType<FileBuildResult.Built>(result));
    }

    [Fact]
    public async Task A_finished_build_frees_its_slot_for_the_next()
    {
        var limiter = Limiter(1, TimeSpan.Zero);
        await limiter.BuildAsync(BuildContent);

        var result = await limiter.BuildAsync(BuildContent);

        Assert.IsType<FileBuildResult.Built>(result);
    }

    [Fact]
    public async Task A_build_that_throws_is_reported_as_failed_and_frees_its_slot()
    {
        var limiter = Limiter(1, TimeSpan.Zero);

        var failed = await limiter.BuildAsync(() => throw new InvalidOperationException("The template changed."));
        var next = await limiter.BuildAsync(BuildContent);

        Assert.IsType<FileBuildResult.Failed>(failed);
        Assert.IsType<FileBuildResult.Built>(next);
    }

    [Fact]
    public async Task A_build_cancelled_while_waiting_for_a_slot_is_cancelled_rather_than_reported()
    {
        var limiter = Limiter(1, TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.BuildAsync(BuildContent, cancellation.Token));
    }
}
