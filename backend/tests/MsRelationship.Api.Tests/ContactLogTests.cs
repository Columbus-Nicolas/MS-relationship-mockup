using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Features.Contacts;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ContactLogTests
{
    private readonly PostgresFixture _pg;
    public ContactLogTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task An_empty_log_answers_null_not_a_date()
    {
        await using var db = _pg.NewContext();
        var (actor, _, p) = await Fixtures.Trio(db);
        var svc = new ContactService(db, new FakeCurrentUser(actor.Id));

        Assert.Null(await svc.LastContactAsync(p.Id));
    }

    [Fact]
    public async Task Anybody_may_register_a_contact_not_only_the_owner()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 2, null);
        await new OwnerService(db).SetOwnerAsync(p.Id, u.Id);

        // actor is not the owner, and registers anyway
        var entry = await new ContactService(db, new FakeCurrentUser(actor.Id)).RegisterAsync(p.Id);

        // RegisterAsync returns null for a profile that is gone or merged away;
        // p is neither, so a null here would be the guard firing wrongly.
        Assert.NotNull(entry);
        Assert.Equal(actor.Id, entry.RegisteredByUserId);
    }

    [Fact]
    public async Task The_newest_entry_is_the_last_contact_and_nothing_resets()
    {
        await using var db = _pg.NewContext();
        var (actor, _, p) = await Fixtures.Trio(db);
        var svc = new ContactService(db, new FakeCurrentUser(actor.Id));

        await svc.RegisterAsync(p.Id, new DateOnly(2026, 5, 4));
        await svc.RegisterAsync(p.Id, new DateOnly(2026, 8, 19));

        var last = await svc.LastContactAsync(p.Id);
        Assert.Equal(new DateOnly(2026, 8, 19), last!.ContactedOn);
        Assert.Equal(2, await db.ContactEntries.CountAsync(c => c.MsProfileId == p.Id));
    }
}
