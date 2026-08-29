using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MatchTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_exact_identity_key_is_the_top_match()
    {
        await using var db = fixture.NewContext();
        var existing = await Seed.ProfileAsync(db, "Nikhil Makkar");
        var matcher = new MsProfileMatcher(db);

        var matches = await matcher.FindAsync("Nikhil Makkar", null, "Microsoft");

        Assert.Equal(existing.Id, matches[0].Id);
        Assert.Equal(1.0, matches[0].Confidence);
        Assert.Equal("identity", matches[0].Reason);
    }

    [Fact]
    public async Task A_near_miss_on_spelling_still_surfaces()
    {
        await using var db = fixture.NewContext();
        var existing = await Seed.ProfileAsync(db, "Chandana Ramesh");
        var matcher = new MsProfileMatcher(db);

        var matches = await matcher.FindAsync("Chandanna Ramesh", null, "Microsoft");

        Assert.Contains(matches, m => m.Id == existing.Id && m.Reason == "similar-name");
    }

    [Fact]
    public async Task A_merged_away_record_is_never_offered()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db, "Bo Larsen");
        var tombstone = new MsProfile
        {
            Id = Guid.NewGuid(), Name = "Bo Larsen", Organization = "Microsoft", MergedIntoId = survivor.Id
        };
        db.MsProfiles.Add(tombstone);
        await db.SaveChangesAsync();

        var matches = await new MsProfileMatcher(db).FindAsync("Bo Larsen", null, "Microsoft");

        Assert.DoesNotContain(matches, m => m.Id == tombstone.Id);
    }
}
