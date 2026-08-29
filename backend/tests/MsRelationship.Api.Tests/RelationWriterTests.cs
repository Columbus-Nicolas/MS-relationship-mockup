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
}
