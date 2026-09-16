using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MergeTests
{
    private readonly PostgresFixture _pg;
    public MergeTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Relations_contacts_and_customers_move_to_the_survivor()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Anne Berg", null, "Microsoft Denmark");
        var customer = new Customer { Name = "Hempel A/S " + Guid.NewGuid().ToString("N")[..6] };
        db.AddRange(duplicate, customer);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));
        await writer.SetAsync(u.Id, duplicate.Id, 2, "knows them well");
        db.ContactEntries.Add(new ContactEntry
        {
            MsProfileId = duplicate.Id, RegisteredByUserId = actor.Id,
            ContactedOn = new DateOnly(2026, 6, 1)
        });
        db.MsProfileCustomers.Add(new MsProfileCustomer
        {
            MsProfileId = duplicate.Id, CustomerId = customer.Id
        });
        await db.SaveChangesAsync();

        var result = await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        Assert.True(await db.Relations.AnyAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id));
        Assert.True(await db.ContactEntries.AnyAsync(c => c.MsProfileId == survivor.Id));
        Assert.True(await db.MsProfileCustomers.AnyAsync(c => c.MsProfileId == survivor.Id));
        // IgnoreQueryFilters, on a fresh context: the duplicate is now a tombstone,
        // which the global query filter hides. FindAsync on db would have hidden
        // the change of subject too — it hands back the tracked instance without
        // querying, so this would have asserted against memory, not Postgres.
        await using var check = _pg.NewContext();
        Assert.Equal(survivor.Id, (await check.MsProfiles.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
    }

    [Fact]
    public async Task When_both_held_the_same_pair_the_newer_relation_survives()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Bo Larsen", null, "Microsoft Denmark " + Guid.NewGuid());
        db.Add(duplicate);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));
        await writer.SetAsync(u.Id, survivor.Id, 0, "older");
        await Task.Delay(10);
        await writer.SetAsync(u.Id, duplicate.Id, 3, "newer");

        await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id);
        Assert.Equal((short)3, kept.Score);
        // the losing side is not lost, only superseded: three history rows now
        // belong to the survivor — the "older" one written directly against it,
        // the "newer" one moved off the duplicate, and the merge's own row
        // recording that the survivor's score was overwritten — and none remain
        // on the duplicate.
        Assert.Equal(3, await db.RelationHistory.CountAsync(h => h.MsProfileId == survivor.Id && h.ColumbusUserId == u.Id));
        Assert.False(await db.RelationHistory.AnyAsync(h => h.MsProfileId == duplicate.Id));
    }

    /// The stage's exit criterion is that every change is attributable to a
    /// person and a time (FR-32). A merge overwriting the survivor's score used
    /// to be the exception: the value changed with no history row and no actor,
    /// and the moved rows only ever recorded the duplicate's past, never that
    /// the survivor's own row had moved.
    [Fact]
    public async Task Superseding_the_survivors_score_is_recorded_with_both_values_and_an_actor()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Erik Lund", null, "Microsoft Denmark " + Guid.NewGuid());
        db.Add(duplicate);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(u.Id));
        await writer.SetAsync(u.Id, survivor.Id, -1, "barely know them");
        await Task.Delay(10);
        await writer.SetAsync(u.Id, duplicate.Id, 3, "on first-name terms");

        await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        // A fresh context: what Postgres stored, not what the merger tracked.
        await using var check = _pg.NewContext();
        var merged = await check.RelationHistory
            .SingleAsync(h => h.MsProfileId == survivor.Id && h.ChangeType == RelationChangeType.Merged);
        Assert.Equal((short)-1, merged.OldScore);
        Assert.Equal("barely know them", merged.OldNote);
        Assert.Equal((short)3, merged.NewScore);
        Assert.Equal("on first-name terms", merged.NewNote);
        Assert.Equal(u.Id, merged.ColumbusUserId);          // whose assessment moved
        Assert.Equal(actor.Id, merged.ChangedByUserId);     // who ran the merge — not the same person

        // And it is the newest row, so an undo reading this table sees the merge
        // rather than the moved "Created 3" underneath it.
        var newest = await check.RelationHistory
            .Where(h => h.MsProfileId == survivor.Id)
            .OrderByDescending(h => h.ChangedAt).FirstAsync();
        Assert.Equal(RelationChangeType.Merged, newest.ChangeType);
    }

    /// When the survivor's own relation wins, nothing about it changed, so it
    /// earns no row — the duplicate's history moves across intact and says all
    /// there is to say. A merge that logged an unchanged value would put noise
    /// in the table undo is built from.
    [Fact]
    public async Task A_merge_that_changes_no_score_writes_no_merge_row()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Frida Holm", null, "Microsoft Denmark " + Guid.NewGuid());
        db.Add(duplicate);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(u.Id));
        await writer.SetAsync(u.Id, duplicate.Id, 1, "older");
        await Task.Delay(10);
        await writer.SetAsync(u.Id, survivor.Id, 2, "newer");   // the survivor's is the newer one

        await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        await using var check = _pg.NewContext();
        Assert.Equal((short)2, (await check.Relations
            .SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id)).Score);
        Assert.False(await check.RelationHistory
            .AnyAsync(h => h.MsProfileId == survivor.Id && h.ChangeType == RelationChangeType.Merged));
    }

    [Fact]
    public async Task A_merge_with_nobody_signed_in_is_refused()
    {
        await using var db = _pg.NewContext();
        var (_, _, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Gitte Roed", null, "Microsoft Denmark " + Guid.NewGuid());
        db.Add(duplicate);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MsProfileMerger(db, new FakeCurrentUser(null)).MergeAsync(survivor.Id, duplicate.Id));

        // Refused, not rolled back: the check runs before any query or
        // transaction, so the duplicate is untouched rather than half-merged.
        await using var check = _pg.NewContext();
        Assert.Null((await check.MsProfiles.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
    }

    [Fact]
    public async Task A_profile_cannot_be_merged_into_itself()
    {
        await using var db = _pg.NewContext();
        var (actor, _, p) = await Fixtures.Trio(db);
        var result = await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(p.Id, p.Id);
        Assert.False(result.Merged);
        Assert.NotNull(result.Refused);
    }

    // A survivor that has itself already been merged away is a tombstone
    // MsProfileMatcher will never return (Task 7 excludes MergedIntoId != null
    // from both its queries). Merging onto one would move live data onto a row
    // nothing reading profiles can find — stranded, not lost, but invisible.
    [Fact]
    public async Task A_profile_already_merged_away_cannot_be_a_survivor()
    {
        await using var db = _pg.NewContext();
        var (actor, _, a) = await Fixtures.Trio(db);
        var b = MsProfile.Create("Grandparent Tombstone", null, "Microsoft Denmark " + Guid.NewGuid());
        var c = MsProfile.Create("Third Profile", null, "Microsoft Denmark " + Guid.NewGuid());
        db.AddRange(b, c);
        await db.SaveChangesAsync();

        var merger = new MsProfileMerger(db, new FakeCurrentUser(actor.Id));
        Assert.True((await merger.MergeAsync(a.Id, b.Id)).Merged); // b is now a tombstone pointing at a

        var result = await merger.MergeAsync(b.Id, c.Id); // b, itself merged away, cannot be a survivor

        Assert.False(result.Merged);
        Assert.NotNull(result.Refused);
        // c must not have been silently moved onto the tombstoned b. Read back
        // through a fresh context, and past the query filter, so this is what
        // Postgres holds rather than what the merger left tracked.
        await using var check = _pg.NewContext();
        Assert.Null((await check.MsProfiles.IgnoreQueryFilters()
            .SingleAsync(p => p.Id == c.Id)).MergedIntoId);
    }

    // The brief's first test names relations, contacts and customers; domain
    // membership is the fifth carried thing and gets no coverage there, so it
    // is proven moved on its own here.
    [Fact]
    public async Task Domain_membership_moves_to_the_survivor()
    {
        await using var db = _pg.NewContext();
        var (actor, _, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Carsten Holm", null, "Microsoft Denmark " + Guid.NewGuid());
        var domain = new Domain { Name = "Partner Leadership " + Guid.NewGuid().ToString("N")[..6] };
        db.AddRange(duplicate, domain);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = domain.Id });
        await db.SaveChangesAsync();

        var result = await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        Assert.True(await db.MsProfileDomains.AnyAsync(x => x.MsProfileId == survivor.Id && x.DomainId == domain.Id));
        Assert.False(await db.MsProfileDomains.AnyAsync(x => x.MsProfileId == duplicate.Id));
    }

    // Both composite-key link tables can hold a row for the same customer or
    // domain on both sides of a merge. Moving the duplicate's row unconditionally
    // would violate the (MsProfileId, CustomerId/DomainId) primary key, so this
    // proves the merge collapses the pair into the survivor's single row instead
    // of throwing.
    [Fact]
    public async Task A_link_shared_by_both_profiles_is_not_duplicated()
    {
        await using var db = _pg.NewContext();
        var (actor, _, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Ida Krogh", null, "Microsoft Denmark " + Guid.NewGuid());
        var customer = new Customer { Name = "Vestas " + Guid.NewGuid().ToString("N")[..6] };
        var domain = new Domain { Name = "Manufacturing " + Guid.NewGuid().ToString("N")[..6] };
        db.AddRange(duplicate, customer, domain);
        db.MsProfileCustomers.AddRange(
            new MsProfileCustomer { MsProfileId = survivor.Id, CustomerId = customer.Id },
            new MsProfileCustomer { MsProfileId = duplicate.Id, CustomerId = customer.Id });
        db.MsProfileDomains.AddRange(
            new MsProfileDomain { MsProfileId = survivor.Id, DomainId = domain.Id },
            new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = domain.Id });
        await db.SaveChangesAsync();

        var result = await new MsProfileMerger(db, new FakeCurrentUser(actor.Id)).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        // Not just "one row survives" — it must be the survivor's row, not the
        // duplicate's; the duplicate's link is what should have been dropped.
        Assert.Equal(1, await db.MsProfileCustomers.CountAsync(x => x.CustomerId == customer.Id && x.MsProfileId == survivor.Id));
        Assert.Equal(1, await db.MsProfileDomains.CountAsync(x => x.DomainId == domain.Id && x.MsProfileId == survivor.Id));
    }
}
