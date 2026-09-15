using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationTests
{
    private readonly PostgresFixture _pg;
    public RelationTests(PostgresFixture pg) => _pg = pg;

    private static async Task<(ColumbusUser, MsProfile)> Pair(AppDbContext db, string tag)
    {
        var u = new ColumbusUser { Name = $"User {tag}", Email = $"{tag}@columbusglobal.example" };
        var p = MsProfile.Create($"MS {tag}", $"{tag}@ms.example", null);
        db.AddRange(u, p);
        await db.SaveChangesAsync();
        return (u, p);
    }

    [Theory]
    [InlineData((short)-4)]
    [InlineData((short)4)]
    public async Task A_score_outside_the_scale_is_refused(short score)
    {
        await using var db = _pg.NewContext();
        var (u, p) = await Pair(db, Guid.NewGuid().ToString("N")[..8]);
        db.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = score });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_person_may_know_many_and_be_known_by_many()
    {
        await using var db = _pg.NewContext();
        var (u1, p1) = await Pair(db, "aa" + Guid.NewGuid().ToString("N")[..6]);
        var (u2, p2) = await Pair(db, "bb" + Guid.NewGuid().ToString("N")[..6]);
        db.Relations.AddRange(
            new Relation { ColumbusUserId = u1.Id, MsProfileId = p1.Id, Score = 3 },
            new Relation { ColumbusUserId = u1.Id, MsProfileId = p2.Id, Score = 1 },
            new Relation { ColumbusUserId = u2.Id, MsProfileId = p1.Id, Score = -1 });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Relations.CountAsync(r => r.ColumbusUserId == u1.Id));
        Assert.Equal(2, await db.Relations.CountAsync(r => r.MsProfileId == p1.Id));
    }

    [Fact]
    public async Task The_same_pair_cannot_be_registered_twice()
    {
        await using var db = _pg.NewContext();
        var (u, p) = await Pair(db, "cc" + Guid.NewGuid().ToString("N")[..6]);
        db.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = 2 });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
