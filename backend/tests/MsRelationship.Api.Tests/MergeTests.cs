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

        var result = await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        Assert.True(await db.Relations.AnyAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id));
        Assert.True(await db.ContactEntries.AnyAsync(c => c.MsProfileId == survivor.Id));
        Assert.True(await db.MsProfileCustomers.AnyAsync(c => c.MsProfileId == survivor.Id));
        Assert.Equal(survivor.Id, (await db.MsProfiles.FindAsync(duplicate.Id))!.MergedIntoId);
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

        await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id);
        Assert.Equal((short)3, kept.Score);
        // the losing side is not lost, only superseded
        Assert.True(await db.RelationHistory.AnyAsync(h => h.MsProfileId == survivor.Id));
    }

    [Fact]
    public async Task A_profile_cannot_be_merged_into_itself()
    {
        await using var db = _pg.NewContext();
        var (_, _, p) = await Fixtures.Trio(db);
        var result = await new MsProfileMerger(db).MergeAsync(p.Id, p.Id);
        Assert.False(result.Merged);
        Assert.NotNull(result.Refused);
    }

    // The brief's first test names relations, contacts and customers; domain
    // membership is the fifth carried thing and gets no coverage there, so it
    // is proven moved on its own here.
    [Fact]
    public async Task Domain_membership_moves_to_the_survivor()
    {
        await using var db = _pg.NewContext();
        var (_, _, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Carsten Holm", null, "Microsoft Denmark " + Guid.NewGuid());
        var domain = new Domain { Name = "Partner Leadership " + Guid.NewGuid().ToString("N")[..6] };
        db.AddRange(duplicate, domain);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = domain.Id });
        await db.SaveChangesAsync();

        var result = await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

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
        var (_, _, survivor) = await Fixtures.Trio(db);
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

        var result = await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        Assert.Equal(1, await db.MsProfileCustomers.CountAsync(x => x.CustomerId == customer.Id));
        Assert.Equal(1, await db.MsProfileDomains.CountAsync(x => x.DomainId == domain.Id));
    }
}
