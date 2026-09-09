using kisatsingen.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace kisatsingen.Tests.Data;

// One Postgres container for the whole test run. Starting a container costs a
// second or two, so it is shared via a collection fixture and each test resets
// the data instead of getting its own container.
public sealed class PostgresFixture : IAsyncLifetime
{
    // Image, database and credentials mirror compose.yml. They have to: the
    // mounted init script hardcodes both the database name and the owning role,
    // so a mismatch here fails during container init rather than in a test.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("kisatsingen_dev_db")
        .WithUsername("local_user")
        .WithPassword("local_password")
        .WithResourceMapping(
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "local-db-init", "01-init-permissions.sql")),
            "/docker-entrypoint-initdb.d/")
        .Build();

    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Factory = new ContainerDbContextFactory(_container.GetConnectionString());

        // Migrate rather than EnsureCreated, so the migrations themselves are
        // under test and not bypassed.
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    // Truncating Chats cascades to every table with a foreign key to it, so this
    // stays correct as new child tables are added.
    public async Task ResetAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "Chats" CASCADE""");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private sealed class ContainerDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            return new AppDbContext(options);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
