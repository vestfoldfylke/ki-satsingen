using Microsoft.EntityFrameworkCore.Design;

namespace kisatsingen.Data;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = Host.CreateApplicationBuilder(args).Configuration;
        return AppDbContext.CreateForMigrations(config);
    }

}
