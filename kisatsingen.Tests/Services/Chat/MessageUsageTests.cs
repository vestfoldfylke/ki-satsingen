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

    // Derived, so the total can never disagree with the two counts shown beside it.
    [Fact]
    public void The_total_is_the_combined_input_plus_the_combined_output()
    {
        var usage = new MessageUsage(900, 100, 1200, 40);

        Assert.Equal(usage.CombinedInputTokens + usage.CombinedOutputTokens, usage.CombinedTotalTokens);
    }

    [Fact]
    public void Usage_with_nothing_estimated_is_not_marked_as_estimated()
    {
        var usage = new MessageUsage(900, 100);

        Assert.False(usage.IsEstimated);
    }

    // Summed conversation usage carries zeros rather than nulls.
    [Fact]
    public void An_estimate_of_zero_is_not_marked_as_estimated()
    {
        var usage = new MessageUsage(900, 100, 0, 0);

        Assert.False(usage.IsEstimated);
    }

    [Fact]
    public void A_count_neither_reported_nor_estimated_stays_absent()
    {
        var usage = new MessageUsage(null, null, 50, null);

        Assert.Null(usage.CombinedOutputTokens);
    }
}
