using kisatsingen.Security;
using Xunit;

namespace kisatsingen.Tests.Security;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void The_production_policy_allows_nothing_from_other_origins()
    {
        var policy = ContentSecurityPolicy.Build(isDevelopment: false);

        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; style-src-elem 'self'; img-src 'self' data:; " +
            "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'",
            policy);
    }
}
