namespace kisatsingen.Constants;

public static class AppConstants
{
    private const string ContributorRole = "Contributor";

    public const string AdminRole = "Administrator";

    public static string[] ContributionRoles => [AdminRole, ContributorRole];
}