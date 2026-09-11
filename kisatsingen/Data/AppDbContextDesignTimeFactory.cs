using Microsoft.EntityFrameworkCore.Design;

namespace kisatsingen.Data;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Only invoked by EF tooling on a developer machine, so default to
    // Development. Env-var overrides (DOTNET_ENVIRONMENT, ConnectionStrings__*)
    // still win because Host layers env vars on top.
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            EnvironmentName = Environments.Development,
        }).Configuration;

        return AppDbContext.CreateForMigrations(config);
    }
}
