using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Relations;

/// The intended way in and out of the relations table (FR-32): every caller
/// that changes a Relation should go through SetAsync/RemoveAsync so the
/// change carries a history row. Nothing structural enforces that — db.Relations
/// is a public DbSet like any other, so this is a convention this codebase
/// holds to, not a guarantee the database or AppDbContext makes.
public class RelationWriter(AppDbContext db, ICurrentUser me)
{
    public async Task<Relation?> SetAsync(Guid columbusUserId, Guid msProfileId, short score, string? note)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A change needs somebody to attribute it to.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var existing = await db.Relations
            .FirstOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);

        var history = new RelationHistory
        {
            ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
            OldScore = existing?.Score, OldNote = existing?.Note,
            NewScore = score, NewNote = note,
            ChangeType = existing is null ? RelationChangeType.Created : RelationChangeType.Updated,
            ChangedByUserId = actor
        };

        if (existing is null)
        {
            existing = new Relation
            {
                ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
                Score = score, Note = note
            };
            db.Relations.Add(existing);
        }
        else
        {
            existing.Score = score;
            existing.Note = note;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        db.RelationHistory.Add(history);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return existing;
    }

    public async Task<Relation?> RemoveAsync(Guid columbusUserId, Guid msProfileId)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A change needs somebody to attribute it to.");

        var existing = await db.Relations
            .FirstOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);
        if (existing is null) return null;   // removing what is not there is a no-op, not an error

        await using var tx = await db.Database.BeginTransactionAsync();
        db.RelationHistory.Add(new RelationHistory
        {
            ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
            OldScore = existing.Score, OldNote = existing.Note,
            NewScore = null, NewNote = null,
            ChangeType = RelationChangeType.Removed,
            ChangedByUserId = actor
        });
        db.Relations.Remove(existing);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return existing;
    }
}
