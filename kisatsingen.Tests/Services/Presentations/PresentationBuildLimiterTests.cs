using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationBuildLimiterTests
{
    private static readonly PresentationSpec Spec = new("Budsjett", PresentationTheme.BarkNype, [new(SlideKind.Chapter, "Innledning")]);

    private static PresentationBuildLimiter Limiter(int maxConcurrentBuilds, TimeSpan maxWait) =>
        new(new VestfoldPresentationBuilder(), new PresentationBuildLimiterOptions(maxConcurrentBuilds, maxWait));

    [Fact]
    public async Task A_build_with_a_free_slot_gives_the_presentation()
    {
        using var limiter = Limiter(1, TimeSpan.FromSeconds(30));

        var result = await limiter.BuildAsync(Spec);

        Assert.NotEmpty(Assert.IsType<PresentationBuildResult.Built>(result).Content);
    }

    // The first call takes the only slot before it returns, and holds it for
    // the whole build, so the second finds no slot free.
    [Fact]
    public async Task A_build_that_finds_every_slot_taken_for_the_whole_wait_is_turned_away()
    {
        using var limiter = Limiter(1, TimeSpan.Zero);

        var first = limiter.BuildAsync(Spec);
        var second = await limiter.BuildAsync(Spec);
        await first;

        Assert.IsType<PresentationBuildResult.Busy>(second);
    }

    [Fact]
    public async Task A_build_waiting_for_a_slot_gets_it_when_the_one_before_finishes()
    {
        using var limiter = Limiter(1, TimeSpan.FromSeconds(30));

        var results = await Task.WhenAll(
            limiter.BuildAsync(Spec),
            limiter.BuildAsync(Spec));

        Assert.All(results, result => Assert.IsType<PresentationBuildResult.Built>(result));
    }

    [Fact]
    public async Task A_finished_build_frees_its_slot_for_the_next()
    {
        using var limiter = Limiter(1, TimeSpan.Zero);
        await limiter.BuildAsync(Spec);

        var result = await limiter.BuildAsync(Spec);

        Assert.IsType<PresentationBuildResult.Built>(result);
    }
}
