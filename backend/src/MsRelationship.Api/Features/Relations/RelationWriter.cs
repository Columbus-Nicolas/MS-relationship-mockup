using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Relations;

/// <summary>
/// The only sanctioned way to mutate <see cref="Relation"/> rows. Every write is paired,
/// in the same <see cref="AppDbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
/// call, with an append-only <see cref="RelationHistory"/> row — so the relation row and its
/// history entry either both land or neither does. Controllers and other callers must go
/// through this class rather than touching <see cref="AppDbContext.Relations"/> directly
/// (FR-09, see the Global Constraint in the backend-foundation plan).
/// </summary>
public class RelationWriter(AppDbContext db)
{
    public async Task<Relation> UpsertAsync(
        Guid columbusUserId, Guid msProfileId, int score, string note, Guid changedBy, Guid? submissionId = null)
    {
        var existing = await db.Relations
            .SingleOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);

        var history = new RelationHistory
        {
            Id = Guid.NewGuid(),
            ColumbusUserId = columbusUserId,
            MsProfileId = msProfileId,
            OldScore = existing?.Score,
            OldNote = existing?.Note,
            NewScore = score,
            NewNote = note,
            ChangeType = existing is null ? RelationChangeType.Created : RelationChangeType.Updated,
            ChangedByUserId = changedBy,
            SubmissionId = submissionId
        };

        if (existing is null)
        {
            existing = new Relation
            {
                Id = Guid.NewGuid(), ColumbusUserId = columbusUserId, MsProfileId = msProfileId
            };
            db.Relations.Add(existing);
        }

        existing.Score = score;
        existing.Note = note;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        db.RelationHistory.Add(history);
        await db.SaveChangesAsync();
        return existing;
    }

    public async Task RemoveAsync(Guid columbusUserId, Guid msProfileId, Guid changedBy, Guid? submissionId = null)
    {
        var existing = await db.Relations
            .SingleOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);
        if (existing is null) return;

        db.RelationHistory.Add(new RelationHistory
        {
            Id = Guid.NewGuid(),
            ColumbusUserId = columbusUserId,
            MsProfileId = msProfileId,
            OldScore = existing.Score,
            OldNote = existing.Note,
            ChangeType = RelationChangeType.Removed,
            ChangedByUserId = changedBy,
            SubmissionId = submissionId
        });

        db.Relations.Remove(existing);
        await db.SaveChangesAsync();
    }
}
