using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class HistoryAppendOnlyTests
{
    private readonly PostgresFixture _pg;
    public HistoryAppendOnlyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task History_rows_cannot_be_edited_or_deleted_through_the_context()
    {
        await using var db = _pg.NewContext();
        // Append-only is enforced by the context, not by remembering not to call Remove.
        var row = new RelationHistory
        {
            ColumbusUserId = Guid.NewGuid(), MsProfileId = Guid.NewGuid(),
            NewScore = 1, ChangeType = RelationChangeType.Created,
            ChangedByUserId = Guid.NewGuid()
        };
        db.RelationHistory.Add(row);
        await db.SaveChangesAsync();

        db.RelationHistory.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void History_rows_cannot_be_edited_or_deleted_through_SaveChanges_bool()
    {
        // DbContext.SaveChanges() forwards to SaveChanges(bool) internally, so a guard
        // placed only on the parameterless overload still sees this path. But a caller
        // who invokes SaveChanges(bool) directly must not find a side door here.
        using var db = _pg.NewContext();
        var row = new RelationHistory
        {
            ColumbusUserId = Guid.NewGuid(), MsProfileId = Guid.NewGuid(),
            NewScore = 1, ChangeType = RelationChangeType.Created,
            ChangedByUserId = Guid.NewGuid()
        };
        db.RelationHistory.Add(row);
        db.SaveChanges(acceptAllChangesOnSuccess: true);

        db.RelationHistory.Remove(row);
        Assert.Throws<InvalidOperationException>(
            () => db.SaveChanges(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public async Task History_rows_cannot_be_edited_or_deleted_through_SaveChangesAsync_bool()
    {
        // Same gap on the async side: SaveChangesAsync(bool, CancellationToken) must
        // guard on its own, not merely because SaveChangesAsync(CancellationToken)
        // happens to forward to it.
        await using var db = _pg.NewContext();
        var row = new RelationHistory
        {
            ColumbusUserId = Guid.NewGuid(), MsProfileId = Guid.NewGuid(),
            NewScore = 1, ChangeType = RelationChangeType.Created,
            ChangedByUserId = Guid.NewGuid()
        };
        db.RelationHistory.Add(row);
        await db.SaveChangesAsync(acceptAllChangesOnSuccess: true);

        db.RelationHistory.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync(acceptAllChangesOnSuccess: true));
    }
}
