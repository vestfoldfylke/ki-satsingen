using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class MessageUsageTests
{
    [Fact]
    public void Combined_counts_add_the_estimate_to_what_was_reported()
    {
        var usage = new MessageUsage(900, 100, 1200, 40);

        Assert.Equal(((long?)2100, (long?)140), (usage.CombinedInputTokens, usage.CombinedOutputTokens));
    }

    [Fact]
    public void The_total_is_the_combined_input_plus_the_combined_output()
    {
        var usage = new MessageUsage(900, 100, 1200, 40);

        Assert.Equal(usage.CombinedInputTokens + usage.CombinedOutputTokens, usage.CombinedTotalTokens);
    }

    // Zeros as well as nulls: summed conversation usage carries zeros.
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(0L, 0L, false)]
    [InlineData(50L, 0L, true)]
    public void Usage_is_marked_as_estimated_only_when_an_estimate_adds_something(
        long? estimatedInput,
        long? estimatedOutput,
        bool isEstimated)
    {
        var usage = new MessageUsage(900, 100, estimatedInput, estimatedOutput);

        Assert.Equal(isEstimated, usage.IsEstimated);
    }

    // A provider may report input without output; that must not read as 0 output.
    [Fact]
    public void A_count_neither_reported_nor_estimated_stays_absent()
    {
        var usage = new MessageUsage(900, null);

        Assert.Null(usage.CombinedOutputTokens);
    }
}
