using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(4)]
    [InlineData(-4)]
    public async Task Scores_outside_the_scale_are_rejected(int score)
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        db.Relations.Add(new Relation
        {
            Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = score
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    public async Task Scores_at_the_scale_boundary_are_accepted(int score)
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        db.Relations.Add(new Relation
        {
            Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = score
        });

        await db.SaveChangesAsync();

        Assert.Equal(score, await db.Relations
            .Where(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id)
            .Select(r => r.Score)
            .SingleAsync());
    }

    [Fact]
    public async Task One_microsoft_person_takes_many_columbus_relations()
    {
        await using var db = fixture.NewContext();
        var (userA, profile) = await Seed.PairAsync(db);
        var userB = await Seed.UserAsync(db);

        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = userA.Id, MsProfileId = profile.Id, Score = 3 });
        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = userB.Id, MsProfileId = profile.Id, Score = 1 });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Relations.CountAsync(r => r.MsProfileId == profile.Id));
    }

    [Fact]
    public async Task The_same_pair_cannot_be_scored_twice()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = 2 });
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = 3 });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
