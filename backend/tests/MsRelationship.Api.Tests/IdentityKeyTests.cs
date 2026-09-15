using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Tests;

public class IdentityKeyUnitTests
{
    [Fact]
    public void Email_wins_when_it_is_known()
        => Assert.Equal("anne.berg@microsoft.com",
            IdentityKey.For("Anne.Berg@Microsoft.com", "Anne Berg", "Microsoft Denmark"));

    [Fact]
    public void Name_and_organisation_are_the_fallback()
        => Assert.Equal("anne berg|microsoft denmark",
            IdentityKey.For(null, "Anne  Berg", "Microsoft Denmark"));

    [Fact]
    public void The_same_name_at_a_different_organisation_is_a_different_person()
        => Assert.NotEqual(IdentityKey.For(null, "Anne Berg", "Microsoft Denmark"),
                           IdentityKey.For(null, "Anne Berg", "Microsoft Norway"));
}

[Collection("postgres")]
public class IdentityKeyTests
{
    private readonly PostgresFixture _pg;
    public IdentityKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Two_profiles_cannot_share_an_identity_key()
    {
        await using var db = _pg.NewContext();
        db.MsProfiles.Add(MsProfile.Create("Nina Due", "nina.due@microsoft.com", null));
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.MsProfiles.Add(MsProfile.Create("Nina Due", "NINA.DUE@microsoft.com", null));
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Cadence_defaults_to_none()
    {
        await using var db = _pg.NewContext();
        var p = MsProfile.Create("Bo Larsen", null, "Microsoft Denmark");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        // Re-read through a FRESH context: FindAsync on the context that just
        // saved the entity returns the tracked instance without issuing any SQL,
        // which would prove nothing about the HasConversion<string> mapping.
        await using var verify = _pg.NewContext();
        Assert.Equal(ReminderCadence.None, (await verify.MsProfiles.FindAsync(p.Id))!.Cadence);
    }
}
