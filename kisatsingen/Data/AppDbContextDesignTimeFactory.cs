using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace kisatsingen.Data;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        return AppDbContext.CreateForMigrations(config);
    }
}
