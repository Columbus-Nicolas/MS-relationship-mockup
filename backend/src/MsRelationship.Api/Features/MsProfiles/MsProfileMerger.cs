using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MergeResult(bool Merged, string? Refused);

public class MsProfileMerger(AppDbContext db, ICurrentUser me)
{
    /// Relations, contact entries, relation history, customer links and domain
    /// links all move from the duplicate to the survivor. Where both held a
    /// relation from the same Columbus person, the more recently updated one
    /// wins and the other stays in history — a merge must not quietly discard
    /// somebody's assessment (FR-20). The duplicate's owner, if any, is dropped
    /// rather than carried over: an owner must hold a relation on the profile
    /// they own (FR-43), and by the time this returns the duplicate holds none.
    ///
    /// A merge tombstones a person and rewrites their relationships, so it is a
    /// change like any other and has to be attributable to somebody (FR-32). It
    /// refuses to run with nobody signed in, the same way RelationWriter does,
    /// and by the same route — thrown, not returned as a MergeResult: `Refused`
    /// carries domain outcomes a caller should show the user ("Already merged."),
    /// and "nobody is signed in" is not one of those.
    public async Task<MergeResult> MergeAsync(Guid survivorId, Guid duplicateId)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A merge needs somebody to attribute it to.");

        if (survivorId == duplicateId) return new MergeResult(false, "A profile cannot be merged into itself.");

        /* The one place that has to see tombstones: this is what writes them, and
           what refuses to merge onto one. FindAsync cannot carry
           IgnoreQueryFilters, and would also hand back an already-tracked row
           without querying at all, so both lookups are explicit queries. */
        var survivor = await db.MsProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == survivorId);
        var duplicate = await db.MsProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == duplicateId);
        if (survivor is null || duplicate is null) return new MergeResult(false, "Profile not found.");
        if (survivor.MergedIntoId is not null) return new MergeResult(false, "The survivor has itself been merged away.");
        if (duplicate.MergedIntoId is not null) return new MergeResult(false, "Already merged.");

        await using var tx = await db.Database.BeginTransactionAsync();

        var duplicateRelations = await db.Relations.Where(r => r.MsProfileId == duplicateId).ToListAsync();
        foreach (var rel in duplicateRelations)
        {
            var clash = await db.Relations.FirstOrDefaultAsync(
                r => r.MsProfileId == survivorId && r.ColumbusUserId == rel.ColumbusUserId);
            if (clash is null) { rel.MsProfileId = survivorId; continue; }

            if (rel.UpdatedAt > clash.UpdatedAt)
            {
                /* The survivor's own score is about to be overwritten, so it gets
                   a history row of its own. The duplicate's moved history records
                   the duplicate's past re-keyed to the survivor; none of it says
                   that this row changed, when, or on whose authority, and Stage 2's
                   undo is built by reading this table — without this row it would
                   see the moved "Created 3" as the newest change and undo a merge
                   by deleting the relation instead of restoring the old score. */
                db.RelationHistory.Add(new RelationHistory
                {
                    ColumbusUserId = rel.ColumbusUserId, MsProfileId = survivorId,
                    OldScore = clash.Score, OldNote = clash.Note,
                    NewScore = rel.Score, NewNote = rel.Note,
                    ChangeType = RelationChangeType.Merged,
                    ChangedByUserId = actor
                });

                clash.Score = rel.Score; clash.Note = rel.Note; clash.UpdatedAt = rel.UpdatedAt;
            }
            /* The other branch gets no row: the survivor's relation is untouched,
               and the duplicate's — older, and now redundant — keeps every one of
               its own history rows, re-keyed to the survivor below. Nothing about
               the surviving assessment changed, so there is nothing to attribute. */
            db.Relations.Remove(rel);
        }

        await db.ContactEntries.Where(c => c.MsProfileId == duplicateId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MsProfileId, survivorId));

        /* The one sanctioned bypass of the append-only guard in AppDbContext.
           ExecuteUpdateAsync goes around the change tracker, so GuardHistory never
           sees it — that is deliberate here, not a loophole. This re-points which
           profile a history row belongs to now that two rows turned out to be the
           same person; it does not touch what the row records — who acted, when,
           and the score/note before and after are all left exactly as written. */
        await db.RelationHistory.Where(h => h.MsProfileId == duplicateId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.MsProfileId, survivorId));

        foreach (var link in await db.MsProfileCustomers.Where(x => x.MsProfileId == duplicateId).ToListAsync())
        {
            var exists = await db.MsProfileCustomers.AnyAsync(
                x => x.MsProfileId == survivorId && x.CustomerId == link.CustomerId);
            db.MsProfileCustomers.Remove(link);
            if (!exists) db.MsProfileCustomers.Add(new Data.Entities.MsProfileCustomer
            { MsProfileId = survivorId, CustomerId = link.CustomerId });
        }

        foreach (var link in await db.MsProfileDomains.Where(x => x.MsProfileId == duplicateId).ToListAsync())
        {
            var exists = await db.MsProfileDomains.AnyAsync(
                x => x.MsProfileId == survivorId && x.DomainId == link.DomainId);
            db.MsProfileDomains.Remove(link);
            if (!exists) db.MsProfileDomains.Add(new Data.Entities.MsProfileDomain
            { MsProfileId = survivorId, DomainId = link.DomainId });
        }

        /* Kept as a tombstone rather than deleted, so a link or an import that
           still names the old id resolves to the survivor instead of 404ing. */
        duplicate.MergedIntoId = survivorId;
        duplicate.OwnerId = null;
        survivor.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new MergeResult(true, null);
    }
}
