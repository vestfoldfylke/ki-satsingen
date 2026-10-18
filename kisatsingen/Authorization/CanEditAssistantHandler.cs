using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace kisatsingen.Authorization;

// Editing and deleting an assistant. Using one (seeing it, chatting with it) is
// not decided here: the queries only ever return assistants the user may use.
public sealed class CanEditAssistantRequirement(string administratorRole) : IAuthorizationRequirement
{
    public string AdministratorRole { get; } = administratorRole;
}

// The owner, or an administrator when the assistant is organization-wide, so
// one that everyone uses is never stuck with an owner who has left.
public sealed class CanEditAssistantHandler : AuthorizationHandler<CanEditAssistantRequirement, Assistant>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanEditAssistantRequirement requirement,
        Assistant assistant)
    {
        var isOwner = context.User.GetObjectId() == assistant.OwnerId;
        var isAdministratorOfOrganizationWide =
            assistant.Visibility == AssistantVisibility.Organization && context.User.IsInRole(requirement.AdministratorRole);

        if (isOwner || isAdministratorOfOrganizationWide)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
