using kisatsingen.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace kisatsingen.Tests.Data;

// One Postgres container for the whole test run. Starting a container costs a
// second or two, so it is shared via a collection fixture and each test resets
// the data instead of getting its own container.
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string OwnerRole = "local_user";
    private const string ApplicationRole = "kisatsingen_web_app";
    private const string Database = "kisatsingen_dev_db";

    // Image, database and owning role mirror compose.yml. They have to: the
    // mounted init script hardcodes the database name and the role it grants
    // default privileges for, so a mismatch fails during container init rather
    // than in a test.
    //
    // Trust auth means neither role needs a credential to connect. The container
    // is ephemeral, on a random loopback port, and torn down with the run — and
    // it keeps test secrets out of the repository entirely.
    private readonly PostgreSqlContainer _container = BuildContainer();

    // Mounts every *.sql under local-db-init in ordinal order, matching Postgres'
    // own alphabetical execution of files in /docker-entrypoint-initdb.d/ and the
    // *.sql glob the csproj uses to copy them. Hardcoding a single filename here
    // would silently ignore any second script added to keep tests aligned with
    // compose.yml — exactly the drift PostgresFixtureTests exists to catch.
    private static PostgreSqlContainer BuildContainer()
    {
        var builder = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase(Database)
            .WithUsername(OwnerRole)
            .WithEnvironment("POSTGRES_HOST_AUTH_METHOD", "trust");

        var initDir = Path.Combine(AppContext.BaseDirectory, "local-db-init");
        var scripts = Directory.EnumerateFiles(initDir, "*.sql")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (scripts.Length == 0)
        {
            throw new InvalidOperationException($"No *.sql init scripts found under {initDir}. Check the csproj copy step.");
        }

        foreach (var script in scripts)
        {
            builder = builder.WithResourceMapping(new FileInfo(script), "/docker-entrypoint-initdb.d/");
        }

        return builder.Build();
    }

    // What production uses: the low-privilege role, holding only the SELECT /
    // INSERT / UPDATE / DELETE and sequence USAGE the init script grants it.
    // Repository tests go through this, so a missing grant fails here instead of
    // in production.
    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;

    // Owns the schema. Needed for migrations and TRUNCATE, neither of which the
    // application role is granted.
    public IDbContextFactory<AppDbContext> OwnerFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var ownerConnectionString = _container.GetConnectionString();
        OwnerFactory = new ContainerDbContextFactory(ownerConnectionString);
        Factory = new ContainerDbContextFactory(ConnectAs(ownerConnectionString, ApplicationRole));

        // Migrate rather than EnsureCreated, so the migrations themselves are
        // under test and not bypassed. Runs as the owner, which is what makes the
        // init script's ALTER DEFAULT PRIVILEGES apply to the new tables and the
        // entry sequence.
        await using var db = await OwnerFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    // Reuses the container's host, port and database so only the role differs.
    private static string ConnectAs(string ownerConnectionString, string role)
    {
        var source = new NpgsqlConnectionStringBuilder(ownerConnectionString);
        return new NpgsqlConnectionStringBuilder
        {
            Host = source.Host,
            Port = source.Port,
            Database = source.Database,
            Username = role
        }.ConnectionString;
    }

    // Truncating Chats cascades to every table with a foreign key to it, so this
    // stays correct as new child tables are added.
    public async Task ResetAsync()
    {
        await using var db = await OwnerFactory.CreateDbContextAsync();
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
