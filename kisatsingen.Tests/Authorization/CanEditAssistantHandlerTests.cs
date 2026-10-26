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
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ClaimsPrincipal User(Guid objectId, params string[] roles) =>
        UserWithObjectIdClaim(objectId.ToString(), roles);

    private static ClaimsPrincipal UserWithObjectIdClaim(string objectIdClaim, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimConstants.ObjectId, objectIdClaim) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static Assistant AssistantOwnedBy(Guid ownerId, AssistantVisibility visibility) => new()
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

    [Fact]
    public async Task The_owner_may_edit_their_assistant_when_the_object_id_claim_is_uppercase()
    {
        var mayEdit = await MayEditAsync(UserWithObjectIdClaim(OwnerId.ToString().ToUpperInvariant()), AssistantOwnedBy(OwnerId, AssistantVisibility.Private));

        Assert.True(mayEdit);
    }

    [Fact]
    public async Task A_user_whose_object_id_claim_is_not_a_guid_may_not_edit_an_assistant()
    {
        var mayEdit = await MayEditAsync(UserWithObjectIdClaim("not-a-guid"), AssistantOwnedBy(OwnerId, AssistantVisibility.Private));

        Assert.False(mayEdit);
    }
}
