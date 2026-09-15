using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class TaxonomyTests
{
    private readonly PostgresFixture _pg;
    public TaxonomyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_group_can_be_added_without_a_migration()
    {
        await using var db = _pg.NewContext();
        db.MsGroups.Add(new MsGroup { Name = "Fabric Specialist", SortOrder = 99 });
        await db.SaveChangesAsync();

        Assert.True(await db.MsGroups.AnyAsync(g => g.Name == "Fabric Specialist"));
    }

    [Fact]
    public async Task Group_names_are_unique()
    {
        await using var db = _pg.NewContext();
        db.MsGroups.Add(new MsGroup { Name = "Leadership", SortOrder = 1 });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.MsGroups.Add(new MsGroup { Name = "Leadership", SortOrder = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
