using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationWriterTests
{
    private readonly PostgresFixture _pg;
    public RelationWriterTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Creating_a_relation_writes_one_history_row_naming_the_actor()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 2, "Met at the partner day.");

        var history = await db.RelationHistory
            .Where(h => h.MsProfileId == p.Id).SingleAsync();
        Assert.Equal(RelationChangeType.Created, history.ChangeType);
        Assert.Null(history.OldScore);
        Assert.Equal((short)2, history.NewScore);
        Assert.Equal(actor.Id, history.ChangedByUserId);
    }

    [Fact]
    public async Task Changing_a_score_records_both_sides_of_the_change()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 1, null);
        await writer.SetAsync(u.Id, p.Id, 3, "Now a first call.");

        var latest = await db.RelationHistory
            .Where(h => h.MsProfileId == p.Id)
            .OrderByDescending(h => h.ChangedAt).FirstAsync();
        Assert.Equal((short)1, latest.OldScore);
        Assert.Equal((short)3, latest.NewScore);
        Assert.Equal(RelationChangeType.Updated, latest.ChangeType);
    }

    [Fact]
    public async Task Removing_a_relation_leaves_the_history_behind()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 2, null);
        await writer.RemoveAsync(u.Id, p.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == p.Id));
        Assert.Equal(2, await db.RelationHistory.CountAsync(h => h.MsProfileId == p.Id));
    }

    [Fact]
    public async Task A_write_with_nobody_signed_in_is_refused()
    {
        await using var db = _pg.NewContext();
        var (_, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.SetAsync(u.Id, p.Id, 2, null));

        // Refused, not merely rolled back: the actor check runs before any query or
        // transaction, so neither table shows a trace of the attempt.
        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == p.Id));
        Assert.False(await db.RelationHistory.AnyAsync(h => h.MsProfileId == p.Id));
    }
}
