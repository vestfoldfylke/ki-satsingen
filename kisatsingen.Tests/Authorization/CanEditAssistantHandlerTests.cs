using System.Security.Claims;
using kisatsingen.Authorization;
using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Xunit;

namespace kisatsingen.Tests.Authorization;

public sealed class CanEditAssistantHandlerTests
{
    private const string AdministratorRole = "Administrator";
    private const string OwnerId = "owner";
    private const string OtherUserId = "someone-else";

    private static ClaimsPrincipal User(string objectId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimConstants.ObjectId, objectId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static Assistant AssistantOwnedBy(string ownerId, AssistantVisibility visibility) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        Name = "Saksbehandler",
        Instructions = "x",
        Visibility = visibility
    };

    private static async Task<bool> MayEditAsync(ClaimsPrincipal user, Assistant assistant)
    {
        var requirement = new CanEditAssistantRequirement(AdministratorRole);
        var context = new AuthorizationHandlerContext([requirement], user, assistant);

        await new CanEditAssistantHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task The_owner_may_edit_their_private_assistant()
    {
        var mayEdit = await MayEditAsync(User(OwnerId), AssistantOwnedBy(OwnerId, AssistantVisibility.Private));

        Assert.True(mayEdit);
    }

    [Fact]
    public async Task An_administrator_may_edit_another_owners_organization_wide_assistant()
    {
        var mayEdit = await MayEditAsync(User(OtherUserId, AdministratorRole), AssistantOwnedBy(OwnerId, AssistantVisibility.Organization));

        Assert.True(mayEdit);
    }

    [Fact]
    public async Task An_administrator_may_not_edit_another_owners_private_assistant()
    {
        var mayEdit = await MayEditAsync(User(OtherUserId, AdministratorRole), AssistantOwnedBy(OwnerId, AssistantVisibility.Private));

        Assert.False(mayEdit);
    }

    [Fact]
    public async Task Another_user_may_not_edit_an_organization_wide_assistant()
    {
        var mayEdit = await MayEditAsync(User(OtherUserId), AssistantOwnedBy(OwnerId, AssistantVisibility.Organization));

        Assert.False(mayEdit);
    }
}
