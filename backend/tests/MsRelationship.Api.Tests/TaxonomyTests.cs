using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class TaxonomyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_new_group_needs_no_migration()
    {
        await using var db = fixture.NewContext();
        db.MsGroups.Add(new MsGroup { Id = Guid.NewGuid(), Name = "Customer Success" });
        await db.SaveChangesAsync();

        Assert.True(await db.MsGroups.AnyAsync(g => g.Name == "Customer Success"));
    }

    [Fact]
    public async Task Domain_names_are_unique()
    {
        await using var db = fixture.NewContext();
        db.MsDomains.Add(new MsDomain { Id = Guid.NewGuid(), Name = "Duplicated", Owner = "", Description = "" });
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.MsDomains.Add(new MsDomain { Id = Guid.NewGuid(), Name = "Duplicated", Owner = "", Description = "" });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
