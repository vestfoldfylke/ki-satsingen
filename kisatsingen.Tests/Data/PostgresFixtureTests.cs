using kisatsingen.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace kisatsingen.Tests.Data;

// Guards the fixture's promise that the container mirrors compose.yml. If the
// init script stops being mounted, these fail loudly here rather than showing up
// much later as a missing grant on some newly added table or sequence.
[Collection(PostgresCollection.Name)]
public sealed class PostgresFixtureTests(PostgresFixture fixture)
{
    // Without this, a mistake in the fixture's connection string could silently
    // run every repository test as the schema owner, and the privilege coverage
    // those tests are supposed to give would be imaginary.
    [Fact]
    public async Task the_repository_factory_connects_as_the_low_privilege_role()
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();

        var role = await db.Database
            .SqlQuery<string>($"""SELECT current_user AS "Value" """)
            .SingleAsync();

        Assert.Equal("kisatsingen_web_app", role);
    }

    [Fact]
    public async Task the_owner_factory_connects_as_the_schema_owner()
    {
        await using var db = await fixture.OwnerFactory.CreateDbContextAsync();

        var role = await db.Database
            .SqlQuery<string>($"""SELECT current_user AS "Value" """)
            .SingleAsync();

        Assert.Equal("local_user", role);
    }

    // The app role can only draw sequence values because migrations run as the
    // owner and the init script grants USAGE on that owner's future sequences.
    // Three links, so worth asserting directly rather than inferring it.
    [Fact]
    public async Task the_application_role_can_draw_from_the_entry_sequence()
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();

        var next = await db.Database
            .SqlQuery<long>($"""SELECT nextval('chat_entry_seq') AS "Value" """)
            .SingleAsync();

        Assert.True(next > 0);
    }

    [Fact]
    public async Task the_init_script_created_the_low_privilege_application_role()
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();

        var roleCount = await db.Database
            .SqlQuery<long>($"""SELECT count(*) AS "Value" FROM pg_roles WHERE rolname = 'kisatsingen_web_app'""")
            .SingleAsync();

        Assert.Equal(1, roleCount);
    }

    [Fact]
    public async Task the_application_role_has_default_privileges_on_future_sequences()
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();

        var grants = await db.Database
            .SqlQuery<string>($"""
                SELECT unnest(defaclacl)::text AS "Value"
                FROM pg_default_acl acl
                JOIN pg_namespace ns ON ns.oid = acl.defaclnamespace
                WHERE ns.nspname = 'public' AND acl.defaclobjtype = 'S'
                """)
            .ToListAsync();

        Assert.Contains(grants, g => g.StartsWith("kisatsingen_web_app=", StringComparison.Ordinal));
    }
}
