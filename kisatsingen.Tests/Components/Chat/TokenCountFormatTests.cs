using kisatsingen.Components.Chat;
using Xunit;

namespace kisatsingen.Tests.Components.Chat;

// Below 1,000 so the thousands separator of the machine's culture never matters.
public sealed class TokenCountFormatTests
{
    [Fact]
    public void An_estimated_count_is_marked_with_a_tilde()
    {
        Assert.Equal("~900", TokenCountFormat.Format(900, isEstimated: true));
    }

    [Fact]
    public void A_reported_count_is_shown_as_it_is()
    {
        Assert.Equal("900", TokenCountFormat.Format(900, isEstimated: false));
    }
}
