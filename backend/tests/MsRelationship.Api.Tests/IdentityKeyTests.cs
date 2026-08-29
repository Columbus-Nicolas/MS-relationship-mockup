using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class IdentityKeyTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static MsProfile Profile(string name, string? email = null, string org = "Microsoft") =>
        new() { Id = Guid.NewGuid(), Name = name, Email = email, Organization = org };

    [Fact]
    public async Task Email_wins_and_is_normalised()
    {
        await using var db = fixture.NewContext();
        var p = Profile("Nina Due", "  Nina.Due@Microsoft.com ");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        await using var read = fixture.NewContext();
        Assert.Equal("nina.due@microsoft.com", (await read.MsProfiles.SingleAsync(x => x.Id == p.Id)).IdentityKey);
    }

    [Fact]
    public async Task Falls_back_to_name_and_organization()
    {
        await using var db = fixture.NewContext();
        var p = Profile("Claus Iversen", null, "Microsoft");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        await using var read = fixture.NewContext();
        Assert.Equal("claus iversen|microsoft", (await read.MsProfiles.SingleAsync(x => x.Id == p.Id)).IdentityKey);
    }

    [Fact]
    public async Task Live_duplicates_are_rejected()
    {
        await using var db = fixture.NewContext();
        db.MsProfiles.Add(Profile("Kris Rozanka"));
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.MsProfiles.Add(Profile("kris rozanka"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_tombstoned_duplicate_does_not_block_the_survivor()
    {
        await using var db = fixture.NewContext();
        var survivor = Profile("Mauro Dalvit");
        db.MsProfiles.Add(survivor);
        await db.SaveChangesAsync();

        // A merged-away record keeps the same identity key and must not trip the index.
        await using var second = fixture.NewContext();
        second.MsProfiles.Add(new MsProfile
        {
            Id = Guid.NewGuid(), Name = "Mauro Dalvit", Organization = "Microsoft", MergedIntoId = survivor.Id
        });

        await second.SaveChangesAsync();
        Assert.Equal(2, await second.MsProfiles.CountAsync(x => x.Name == "Mauro Dalvit"));
    }
}
