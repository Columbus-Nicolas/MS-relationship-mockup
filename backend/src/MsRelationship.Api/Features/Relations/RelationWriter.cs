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
        existing.UpdatedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        db.RelationHistory.Add(history);
        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Postgres's "timestamp with time zone" has a hard 6-digit (microsecond) precision
    /// ceiling, while .NET's <see cref="DateTimeOffset"/> ticks are 100ns-resolution (one
    /// digit finer) and are almost never evenly divisible by 10. Rounding down here, before
    /// the value is ever assigned to the tracked <see cref="Relation"/>, guarantees the
    /// in-memory value EF snapshots into <c>OriginalValues</c> for the
    /// <see cref="Relation.UpdatedAt"/> concurrency check is bit-for-bit identical to what
    /// Postgres actually persists. Without this, a value carrying sub-microsecond residue
    /// would be truncated by Postgres on write but kept untruncated in EF's in-memory
    /// original-values snapshot, so a second write to the *same* tracked entity through the
    /// *same* <see cref="AppDbContext"/> — e.g. two <see cref="Features.Submissions.SubmissionService.ApproveAsync"/>
    /// items touching the same relation — could compare against a value that no longer
    /// matches the stored row and throw a spurious <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// with no real second writer involved (Task 7b, fix-round-1 finding).
    /// </summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);

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
