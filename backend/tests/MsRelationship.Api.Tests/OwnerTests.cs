using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class OwnerTests
{
    private readonly PostgresFixture _pg;
    public OwnerTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Somebody_holding_a_relation_may_be_made_owner()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 2, null);

        Assert.True(await new OwnerService(db).SetOwnerAsync(p.Id, u.Id));

        // A fresh context, not db: FindAsync on an entity the context already
        // tracks returns the tracked instance without issuing any SQL at all, so
        // asserting through db would only prove OwnerService mutated an object in
        // memory, not that SaveChangesAsync persisted it.
        await using var verify = _pg.NewContext();
        Assert.Equal(u.Id, (await verify.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }

    [Fact]
    public async Task Somebody_with_no_relation_cannot_be_made_owner()
    {
        await using var db = _pg.NewContext();
        var (_, u, p) = await Fixtures.Trio(db);

        Assert.False(await new OwnerService(db).SetOwnerAsync(p.Id, u.Id));

        await using var verify = _pg.NewContext();
        Assert.Null((await verify.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }

    [Fact]
    public async Task An_owner_can_be_cleared()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 1, null);
        await new OwnerService(db).SetOwnerAsync(p.Id, u.Id);

        Assert.True(await new OwnerService(db).SetOwnerAsync(p.Id, null));

        await using var verify = _pg.NewContext();
        Assert.Null((await verify.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }
}
