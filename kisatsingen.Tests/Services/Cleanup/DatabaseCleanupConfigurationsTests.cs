using System.Globalization;
using kisatsingen.Services.Cleanup;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace kisatsingen.Tests.Services.Cleanup;

public sealed class DatabaseCleanupConfigurationsTests
{
    private static readonly TimeSpan ThreeAm = new(3, 0, 0);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    [Fact]
    public void Chat_cleanup_is_enabled_when_both_retention_and_target_time_are_set()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay, TargetTime = ThreeAm };

        Assert.True(configuration.Enabled);
    }

    [Fact]
    public void Chat_cleanup_is_disabled_when_retention_is_missing()
    {
        var configuration = new DatabaseChatCleanupConfiguration { TargetTime = ThreeAm };

        Assert.False(configuration.Enabled);
    }

    [Fact]
    public void Chat_cleanup_is_disabled_when_target_time_is_missing()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay };

        Assert.False(configuration.Enabled);
    }

    [Fact]
    public void A_missing_chat_cleanup_section_passes_validation()
    {
        var configurations = new DatabaseCleanupConfigurations();

        var exception = Record.Exception(configurations.Validate);

        Assert.Null(exception);
    }

    // Guards the bug where a retention of zero made the cutoff "now" and every chat matched it.
    [Fact]
    public void A_retention_of_zero_is_rejected()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = TimeSpan.Zero, TargetTime = ThreeAm };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }

    [Fact]
    public void A_retention_just_under_one_day_is_rejected()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay - TimeSpan.FromTicks(1), TargetTime = ThreeAm };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }

    [Fact]
    public void A_retention_of_exactly_one_day_is_accepted()
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay, TargetTime = ThreeAm };

        var exception = Record.Exception(configuration.Validate);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("-00:00:01")]
    [InlineData("1.00:00:00")]
    public void A_target_time_outside_a_single_day_is_rejected(string targetTime)
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay, TargetTime = TimeSpan.Parse(targetTime, CultureInfo.InvariantCulture) };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("23:59:59")]
    public void A_target_time_within_the_day_is_accepted(string targetTime)
    {
        var configuration = new DatabaseChatCleanupConfiguration { Retention = OneDay, TargetTime = TimeSpan.Parse(targetTime, CultureInfo.InvariantCulture) };

        var exception = Record.Exception(configuration.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void Retention_and_target_time_bind_from_timespan_strings_in_configuration()
    {
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseCleanup:ChatCleanup:Retention"] = "30.00:00:00",
                ["DatabaseCleanup:ChatCleanup:TargetTime"] = "03:00:00",
            })
            .Build();

        var configurations = configurationRoot.GetSection(DatabaseCleanupConfigurations.SectionName).Get<DatabaseCleanupConfigurations>();

        Assert.NotNull(configurations);
        Assert.Equal(TimeSpan.FromDays(30), configurations.ChatCleanup.Retention);
        Assert.Equal(ThreeAm, configurations.ChatCleanup.TargetTime);
    }
}
