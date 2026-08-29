using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationWriterTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creating_then_updating_leaves_two_history_rows()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        await writer.UpsertAsync(user.Id, profile.Id, 1, "first", user.Id);
        await writer.UpsertAsync(user.Id, profile.Id, 3, "second", user.Id);

        var history = await db.RelationHistory
            .Where(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id)
            .OrderBy(h => h.ChangedAt).ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(RelationChangeType.Created, history[0].ChangeType);
        Assert.Null(history[0].OldScore);
        Assert.Equal(1, history[0].NewScore);
        Assert.Equal(RelationChangeType.Updated, history[1].ChangeType);
        Assert.Equal(1, history[1].OldScore);
        Assert.Equal(3, history[1].NewScore);
    }

    [Fact]
    public async Task Removing_records_the_last_known_score()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        await writer.UpsertAsync(user.Id, profile.Id, -2, "strained", user.Id);
        await writer.RemoveAsync(user.Id, profile.Id, user.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id));

        var last = await db.RelationHistory
            .Where(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id)
            .OrderByDescending(h => h.ChangedAt).FirstAsync();

        Assert.Equal(RelationChangeType.Removed, last.ChangeType);
        Assert.Equal(-2, last.OldScore);
        Assert.Null(last.NewScore);
    }

    /// <summary>
    /// Reproduces the lost-update race from the Task 7 review deterministically, with no
    /// threads or sleeps: two <see cref="AppDbContext"/> instances both load the relation
    /// while it is still at its pre-update state, then one writes. Because
    /// <see cref="Relation.UpdatedAt"/> is a concurrency token, the second writer's stale
    /// snapshot no longer matches the row, and EF fails its UPDATE instead of silently
    /// letting a second, stale-based history row land.
    /// </summary>
    [Fact]
    public async Task Concurrent_updates_to_the_same_relation_are_caught()
    {
        await using var seedDb = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(seedDb);
        await new RelationWriter(seedDb).UpsertAsync(user.Id, profile.Id, 1, "initial", user.Id);

        await using var dbA = fixture.NewContext();
        await using var dbB = fixture.NewContext();

        // Both contexts load (and thus track) the relation at its pre-update state before
        // either one saves. EF's identity map means dbB keeps this stale snapshot — including
        // the original UpdatedAt token — even after dbA's write below changes the real row.
        await dbA.Relations.SingleAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id);
        await dbB.Relations.SingleAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id);

        var writerA = new RelationWriter(dbA);
        var writerB = new RelationWriter(dbB);

        await writerA.UpsertAsync(user.Id, profile.Id, 2, "writer A", user.Id);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => writerB.UpsertAsync(user.Id, profile.Id, 3, "writer B", user.Id));

        await using var verifyDb = fixture.NewContext();
        Assert.Equal(2, (await verifyDb.Relations
            .SingleAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id)).Score);

        var updatedHistoryCount = await verifyDb.RelationHistory.CountAsync(h =>
            h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id &&
            h.ChangeType == RelationChangeType.Updated);
        Assert.Equal(1, updatedHistoryCount);
    }
}
