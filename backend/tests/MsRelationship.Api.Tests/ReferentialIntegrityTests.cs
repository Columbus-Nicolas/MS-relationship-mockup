using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

/// Stage 1's deliverable is the data model, so the model has to be able to keep
/// its own invariants rather than trusting every caller to. Before this, only
/// DashboardService.DeleteAsync guarded an orphaning path (FR-26), and it was
/// the only one of about thirteen that was guarded at all.
[Collection("postgres")]
public class ReferentialIntegrityTests
{
    private readonly PostgresFixture _pg;
    public ReferentialIntegrityTests(PostgresFixture pg) => _pg = pg;

    /// The path the review named: FR-26 stops a dashboard being deleted out from
    /// under its domains, but nothing stopped a domain being deleted out from
    /// under its members — the membership rows simply dangled.
    [Fact]
    public async Task A_domain_cannot_be_deleted_while_somebody_is_still_in_it()
    {
        await using var db = _pg.NewContext();
        var (_, _, profile) = await Fixtures.Trio(db);
        var domain = new Domain { Name = "Security " + Guid.NewGuid().ToString("N")[..6] };
        db.Domains.Add(domain);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = profile.Id, DomainId = domain.Id });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.Domains.Remove((await other.Domains.FindAsync(domain.Id))!);
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        // Refused, not cascaded: the membership survives along with the domain.
        Assert.True(await db.MsProfileDomains.AnyAsync(x => x.DomainId == domain.Id));
    }

    /// Both junction tables carried no foreign keys at all, so either column
    /// could name a row that never existed.
    [Fact]
    public async Task A_membership_row_cannot_name_a_profile_that_does_not_exist()
    {
        await using var db = _pg.NewContext();
        var domain = new Domain { Name = "Fabric " + Guid.NewGuid().ToString("N")[..6] };
        db.Domains.Add(domain);
        await db.SaveChangesAsync();

        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = Guid.NewGuid(), DomainId = domain.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_customer_link_cannot_name_a_customer_that_does_not_exist()
    {
        await using var db = _pg.NewContext();
        var (_, _, profile) = await Fixtures.Trio(db);

        db.MsProfileCustomers.Add(new MsProfileCustomer { MsProfileId = profile.Id, CustomerId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_relation_cannot_name_a_profile_that_does_not_exist()
    {
        await using var db = _pg.NewContext();
        var (_, user, _) = await Fixtures.Trio(db);

        db.Relations.Add(new Relation { ColumbusUserId = user.Id, MsProfileId = Guid.NewGuid(), Score = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_contact_entry_cannot_name_a_user_who_does_not_exist()
    {
        await using var db = _pg.NewContext();
        var (_, _, profile) = await Fixtures.Trio(db);

        db.ContactEntries.Add(new ContactEntry
        {
            MsProfileId = profile.Id,
            RegisteredByUserId = Guid.NewGuid(),
            ContactedOn = new DateOnly(2026, 4, 2)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// The deliberate exception, pinned so nobody "completes" the set later.
    /// relation_history carries no foreign keys because the record has to
    /// outlive what it describes: after the profile is gone, the row must still
    /// say what the score was and who set it.
    [Fact]
    public async Task History_outlives_the_profile_it_describes()
    {
        await using var db = _pg.NewContext();
        var (actor, user, profile) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));
        await writer.SetAsync(user.Id, profile.Id, 3, "worth keeping");
        await writer.RemoveAsync(user.Id, profile.Id);

        await using var wipe = _pg.NewContext();
        wipe.MsProfiles.Remove((await wipe.MsProfiles.FindAsync(profile.Id))!);
        await wipe.SaveChangesAsync();

        await using var check = _pg.NewContext();
        Assert.False(await check.MsProfiles.AnyAsync(p => p.Id == profile.Id));
        var history = await check.RelationHistory
            .Where(h => h.MsProfileId == profile.Id)
            .OrderBy(h => h.ChangedAt).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal((short)3, history[0].NewScore);
        Assert.Equal(actor.Id, history[0].ChangedByUserId);
    }
}
