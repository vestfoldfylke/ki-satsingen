using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationDownloadsTests
{
    private static readonly PresentationSpec Spec = new("Budsjett", PresentationTheme.BarkNype, [new(SlideKind.Chapter, "Innledning")]);

    private static PresentationBuildLimiter Limiter() =>
        new(new VestfoldPresentationBuilder(), new PresentationBuildLimiterOptions(1, TimeSpan.FromSeconds(30)));

    [Fact]
    public async Task A_build_while_the_circuit_lives_gives_the_presentation()
    {
        using var limiter = Limiter();
        using var downloads = new PresentationDownloads(limiter);

        var result = await downloads.BuildAsync(Spec);

        Assert.IsType<PresentationBuildResult.Built>(result);
    }

    [Fact]
    public async Task A_build_after_the_circuit_has_ended_is_cancelled()
    {
        using var limiter = Limiter();
        var downloads = new PresentationDownloads(limiter);

        downloads.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloads.BuildAsync(Spec));
        Assert.True(downloads.IsCancelled);
    }

    [Fact]
    public void Downloads_are_not_cancelled_while_the_circuit_lives()
    {
        using var limiter = Limiter();
        using var downloads = new PresentationDownloads(limiter);

        Assert.False(downloads.IsCancelled);
    }
}
