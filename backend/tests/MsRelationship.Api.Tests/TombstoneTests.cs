using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Contacts;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

/// MergedIntoId != null marks a profile merged away. Every read path excluded
/// those rows and no write path did, so a relation, a contact or an owner could
/// still be written onto one — after the merge that would have carried it to the
/// survivor had already run. The score or the contact then sat in the table and
/// never appeared in the product.
[Collection("postgres")]
public class TombstoneTests
{
    private readonly PostgresFixture _pg;
    public TombstoneTests(PostgresFixture pg) => _pg = pg;

    /// A merged-away profile plus the live survivor it points at.
    private async Task<(ColumbusUser Actor, ColumbusUser User, MsProfile Survivor, MsProfile Tombstone)>
        Merged(AppDbContext db)
    {
        var (actor, user, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Tombstone " + Guid.NewGuid().ToString("N")[..8], null, "Microsoft Denmark");
        db.Add(duplicate);
        await db.SaveChangesAsync();

        var merge = await new MsProfileMerger(db, new FakeCurrentUser(actor.Id))
            .MergeAsync(survivor.Id, duplicate.Id);
        Assert.True(merge.Merged);
        return (actor, user, survivor, duplicate);
    }

    [Fact]
    public async Task A_profile_merged_away_is_not_one_the_context_hands_out()
    {
        await using var db = _pg.NewContext();
        var (_, _, _, tombstone) = await Merged(db);

        await using var check = _pg.NewContext();
        Assert.False(await check.MsProfiles.AnyAsync(p => p.Id == tombstone.Id));
        // Still there, and still pointing at the survivor — filtered, not deleted.
        Assert.NotNull(await check.MsProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == tombstone.Id));
    }

    [Fact]
    public async Task A_relation_cannot_be_set_on_a_profile_merged_away()
    {
        await using var db = _pg.NewContext();
        var (actor, user, _, tombstone) = await Merged(db);

        var written = await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(user.Id, tombstone.Id, 3, "would never have been seen");

        Assert.Null(written);
        await using var check = _pg.NewContext();
        Assert.False(await check.Relations.AnyAsync(r => r.MsProfileId == tombstone.Id));
        // And no history row either: refused, not written-then-undone.
        Assert.False(await check.RelationHistory.AnyAsync(h => h.MsProfileId == tombstone.Id));
    }

    [Fact]
    public async Task A_contact_cannot_be_registered_on_a_profile_merged_away()
    {
        await using var db = _pg.NewContext();
        var (actor, _, _, tombstone) = await Merged(db);

        var entry = await new ContactService(db, new FakeCurrentUser(actor.Id)).RegisterAsync(tombstone.Id);

        Assert.Null(entry);
        await using var check = _pg.NewContext();
        Assert.False(await check.ContactEntries.AnyAsync(c => c.MsProfileId == tombstone.Id));
    }

    /// The merge clears the duplicate's owner on its way out, so that nothing
    /// points at a tombstone. Setting one afterwards would undo exactly that.
    [Fact]
    public async Task A_profile_merged_away_cannot_be_given_an_owner()
    {
        await using var db = _pg.NewContext();
        var (actor, user, survivor, tombstone) = await Merged(db);
        // The would-be owner genuinely holds a relation, so FR-43 is not what
        // refuses this — only the tombstone rule is.
        await new RelationWriter(db, new FakeCurrentUser(actor.Id)).SetAsync(user.Id, survivor.Id, 2, null);

        Assert.False(await new OwnerService(db).SetOwnerAsync(tombstone.Id, user.Id));

        await using var check = _pg.NewContext();
        Assert.Null((await check.MsProfiles.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == tombstone.Id)).OwnerId);
    }

    /// Writing to a profile that was never there at all takes the same route as
    /// writing to a tombstone, and gets the same answer.
    [Fact]
    public async Task A_relation_cannot_be_set_on_a_profile_that_never_existed()
    {
        await using var db = _pg.NewContext();
        var (actor, user, _) = await Fixtures.Trio(db);

        Assert.Null(await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(user.Id, Guid.NewGuid(), 1, null));
    }
}
