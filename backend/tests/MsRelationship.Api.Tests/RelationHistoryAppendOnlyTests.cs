using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Proves FR-09's append-only guarantee is enforced structurally by <see cref="AppDbContext"/>
/// itself (via its <c>SaveChanges</c>/<c>SaveChangesAsync</c> overrides), not merely by
/// convention — see the Task 7 review finding this closes.
/// </summary>
[Collection("postgres")]
public class RelationHistoryAppendOnlyTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Updating_a_persisted_history_row_throws()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        // The insert path must still work — this is the "not broken" half of the guarantee.
        await writer.UpsertAsync(user.Id, profile.Id, 1, "first", user.Id);
        var history = await db.RelationHistory
            .SingleAsync(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id);

        history.OldNote = "tampered";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_persisted_history_row_throws()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        // The insert path must still work — this is the "not broken" half of the guarantee.
        await writer.UpsertAsync(user.Id, profile.Id, 1, "first", user.Id);
        var history = await db.RelationHistory
            .SingleAsync(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id);

        db.RelationHistory.Remove(history);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
