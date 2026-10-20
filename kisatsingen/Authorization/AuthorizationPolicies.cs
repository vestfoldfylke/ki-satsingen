namespace kisatsingen.Authorization;

// The authorization policies' names, so a misspelt one fails to compile instead
// of failing when the page runs.
public static class AuthorizationPolicies
{
    public const string CanUseApp = "CanUseApp";
    public const string IsAdministrator = "IsAdministrator";
    public const string CanContributeOrganizationWide = "CanContributeOrganizationWide";
    public const string CanEditAssistant = "CanEditAssistant";
    public const string CanReadMetrics = "CanReadMetrics";
}
