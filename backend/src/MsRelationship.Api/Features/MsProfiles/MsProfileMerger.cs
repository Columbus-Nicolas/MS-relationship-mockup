using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public record MergeResult(bool Merged, string? Refused);

public class MsProfileMerger(AppDbContext db)
{
    /// Relations, contact entries, relation history, customer links and domain
    /// links all move from the duplicate to the survivor. Where both held a
    /// relation from the same Columbus person, the more recently updated one
    /// wins and the other stays in history — a merge must not quietly discard
    /// somebody's assessment (FR-20). The duplicate's owner, if any, is dropped
    /// rather than carried over: an owner must hold a relation on the profile
    /// they own (FR-43), and by the time this returns the duplicate holds none.
    public async Task<MergeResult> MergeAsync(Guid survivorId, Guid duplicateId)
    {
        if (survivorId == duplicateId) return new MergeResult(false, "A profile cannot be merged into itself.");

        var survivor = await db.MsProfiles.FindAsync(survivorId);
        var duplicate = await db.MsProfiles.FindAsync(duplicateId);
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

            if (rel.UpdatedAt > clash.UpdatedAt) { clash.Score = rel.Score; clash.Note = rel.Note; clash.UpdatedAt = rel.UpdatedAt; }
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
