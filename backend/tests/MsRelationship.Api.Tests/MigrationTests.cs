using Microsoft.EntityFrameworkCore;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MigrationTests
{
    private readonly PostgresFixture _pg;
    public MigrationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        await using var db = _pg.NewContext();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }
}
