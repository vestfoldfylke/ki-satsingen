using kisatsingen.Services.Presentations;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations;

public sealed class PresentationFileBuilderTests
{
    private static readonly PresentationSpec Spec = new("Budsjett", PresentationTheme.BarkNype, [new(SlideKind.Chapter, "Innledning")]);

    private static PresentationBuildLimiter Limiter() =>
        new(new VestfoldPresentationBuilder(new PresentationTemplate(), TimeProvider.System), new PresentationBuildLimiterOptions(1, TimeSpan.FromSeconds(30)));

    [Fact]
    public async Task A_build_while_the_circuit_lives_gives_the_presentation()
    {
        var limiter = Limiter();
        using var builder = new PresentationFileBuilder(limiter);

        var result = await builder.BuildAsync(Spec);

        Assert.IsType<PresentationBuildResult.Built>(result);
    }

    [Fact]
    public async Task A_build_after_the_circuit_has_ended_is_cancelled()
    {
        var limiter = Limiter();
        var builder = new PresentationFileBuilder(limiter);

        builder.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(Spec));
        Assert.True(builder.IsCancelled);
    }

    [Fact]
    public void Builds_are_not_cancelled_while_the_circuit_lives()
    {
        var limiter = Limiter();
        using var builder = new PresentationFileBuilder(limiter);

        Assert.False(builder.IsCancelled);
    }
}
