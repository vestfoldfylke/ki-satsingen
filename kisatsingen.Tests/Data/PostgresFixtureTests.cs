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
    [Fact]
    public async Task the_init_script_created_the_low_privilege_application_role()
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();

        var roleCount = await db.Database
            .SqlQuery<int>($"""SELECT count(*) AS "Value" FROM pg_roles WHERE rolname = 'kisatsingen_web_app'""")
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
