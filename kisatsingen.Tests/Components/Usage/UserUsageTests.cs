using kisatsingen.Components.Usage;
using kisatsingen.Data.Entities;
using Xunit;

namespace kisatsingen.Tests.Components.Usage;

public sealed class UserUsageTests
{
    private static readonly UsageByKey<string> Usage = new("object-id", 10, 20, 0, 0, 1);

    [Fact]
    public void The_display_name_is_the_users_name_when_it_is_known()
    {
        var userUsage = new UserUsage(Usage, new UserInfo { Id = "object-id", Name = "Ada" });

        Assert.Equal("Ada", userUsage.DisplayName);
    }

    [Fact]
    public void The_display_name_falls_back_to_the_object_id_when_there_is_no_user_info()
    {
        var userUsage = new UserUsage(Usage, null);

        Assert.Equal("object-id", userUsage.DisplayName);
    }

    [Fact]
    public void The_display_name_falls_back_to_the_object_id_when_the_user_was_not_found()
    {
        var userUsage = new UserUsage(Usage, new UserInfo { Id = "object-id" });

        Assert.Equal("object-id", userUsage.DisplayName);
    }
}
