using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Features.MsProfiles;

/// <summary>
/// Folds duplicate <see cref="MsProfile"/> rows into a chosen survivor (FR-20), the payoff for
/// the duplicate matching of FR-18/FR-19.
///
/// <para><b>Duplicates are tombstoned, never deleted.</b> A merged profile keeps its row and
/// gains a <see cref="MsProfile.MergedIntoId"/> pointing at the survivor, so links and history
/// that still name the old id stay resolvable. Tombstoning is what makes the merge legal at all:
/// the unique index on <c>identity_key</c> is filtered to <c>merged_into_id IS NULL</c>, so a
/// tombstone may freely share its identity with the survivor it points at.</para>
///
/// <para><b>The tombstone graph is always exactly one hop deep.</b> Three rules hold it that
/// way: a survivor must itself be live, an already-tombstoned profile may not be re-merged
/// somewhere else, and when a profile that is already a survivor is later merged away, every
/// tombstone pointing at it is repointed onto the new survivor in the same transaction. So
/// <c>merged_into_id</c> always names a live profile — resolving a stale id is one lookup, never
/// a walk, and never a cycle.</para>
///
/// <para><b>Every relation write goes through <see cref="RelationWriter"/></b> so that each
/// repointed and each discarded value lands in <c>relation_history</c> in the same SaveChanges
/// as the row it describes.</para>
///
/// <para><b>Existing history is not rewritten.</b> <c>relation_history</c> is append-only
/// (FR-09, enforced by <see cref="AppDbContext"/> itself), so the duplicate's pre-merge rows stay
/// on the duplicate's id — turning a past <c>Created</c> into a <c>MergeMoved</c> would falsify
/// the record of what actually happened. The merge only appends. Those rows stay reachable
/// because the tombstone permanently resolves the old id to the survivor.</para>
///
/// <para><b>The whole merge is one transaction.</b> <see cref="RelationWriter"/> saves per call
/// and enlists in the ambient transaction rather than opening its own, so a failure part-way —
/// a refused duplicate, or a <see cref="DbUpdateConcurrencyException"/> from a concurrent writer
/// — rolls back every relation already moved, leaving no half-merged profile behind.</para>
/// </summary>
public class MsProfileMerger(AppDbContext db, RelationWriter writer)
{
    /// <param name="survivorId">The profile to keep. Must exist and must not itself be a tombstone.</param>
    /// <param name="mergeIds">
    /// The duplicates to fold in. Duplicated entries are collapsed; an empty list is a no-op
    /// rather than an error, so a UI that submits an empty selection gets nothing instead of a
    /// failure. Profiles that were already merged into <paramref name="survivorId"/> are skipped,
    /// which makes a repeated merge idempotent. Pass only live profiles: rows already tombstoned
    /// onto one of these duplicates follow it to the survivor by themselves.
    /// </param>
    /// <param name="actorId">The Columbus user credited with the merge in <c>relation_history</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// The survivor or a duplicate does not exist, the survivor is itself tombstoned, a duplicate
    /// was already merged into a different survivor, or the survivor appears in
    /// <paramref name="mergeIds"/>.
    /// </exception>
    public async Task MergeAsync(Guid survivorId, IReadOnlyList<Guid> mergeIds, Guid actorId)
    {
        // Self-merge would tombstone the survivor onto itself: a self-referential row that is
        // neither live nor resolvable, and a cycle for anything that follows merged_into_id.
        if (mergeIds.Contains(survivorId))
            throw new InvalidOperationException("A profile cannot be merged into itself.");

        var survivor = await db.MsProfiles.SingleOrDefaultAsync(p => p.Id == survivorId)
            ?? throw new InvalidOperationException($"Survivor profile {survivorId} was not found.");

        // Merging into a tombstone would build a chain D -> S -> T that nothing else in the
        // system expects. Merge into whatever this row already points at instead.
        if (survivor.MergedIntoId is not null)
            throw new InvalidOperationException(
                $"Survivor profile {survivorId} was already merged into {survivor.MergedIntoId} " +
                "and cannot receive a merge. Merge into the surviving profile instead.");

        var duplicateIds = mergeIds.Distinct().ToList();
        if (duplicateIds.Count == 0) return;

        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var duplicateId in duplicateIds)
        {
            var duplicate = await db.MsProfiles.SingleOrDefaultAsync(p => p.Id == duplicateId)
                ?? throw new InvalidOperationException($"Profile {duplicateId} was not found.");

            // Already folded into this survivor: the work is done, so re-running the merge is a
            // no-op rather than a second pass that would find nothing to move anyway.
            if (duplicate.MergedIntoId == survivorId) continue;

            // Already folded into someone else. Silently repointing it would orphan the earlier
            // merge — the relations it carried are on the *other* survivor and would not follow.
            if (duplicate.MergedIntoId is not null)
                throw new InvalidOperationException(
                    $"Profile {duplicateId} was already merged into {duplicate.MergedIntoId} and " +
                    "cannot be merged again. Merge that surviving profile instead.");

            await MoveRelationsAsync(survivorId, duplicateId, actorId);

            // A pending submission naming the duplicate must land on the survivor when it is
            // approved; left alone it would write a relation to a tombstone.
            await db.SubmissionItems
                .Where(i => i.MsProfileId == duplicateId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.MsProfileId, survivorId));

            await UnionDomainsAsync(survivorId, duplicateId);

            // The tombstone is written and flushed *before* the survivor is touched below. It
            // has to be: filling a blank survivor email from the duplicate rewrites the
            // survivor's generated identity_key to the duplicate's, and the two rows may only
            // share that key once the duplicate has dropped out of the
            // "merged_into_id IS NULL" filtered unique index. One SaveChanges for both would
            // leave the statement order to EF and could hit a unique violation instead.
            duplicate.MergedIntoId = survivorId;
            await db.SaveChangesAsync();

            // Keep the graph one hop deep: anything pointing at this profile now points past it.
            await db.MsProfiles
                .Where(p => p.MergedIntoId == duplicateId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.MergedIntoId, survivorId));

            FillBlanks(survivor, duplicate);
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>
    /// Moves the duplicate's relations onto the survivor, resolving the collision that FR-20's
    /// spec §6 calls out: a Columbus user may hold a relation to <em>both</em> records, and
    /// <c>relations</c> is uniquely indexed on (columbus_user_id, ms_profile_id). Blindly
    /// repointing would raise a unique violation, not a foreign-key one — so the survivor's side
    /// is looked up first and the two values are reconciled rather than collided.
    ///
    /// <para><b>The higher score survives</b> (spec §6). On a tie the survivor's own value stands:
    /// the survivor is the record the operator chose to keep, and nothing is gained by swapping in
    /// an equal-strength value from the record they discarded.</para>
    ///
    /// <para>Every outcome is recorded. The change type describes what happened to the value that
    /// lived on that row: <see cref="RelationChangeType.MergeMoved"/> where a value ended up on the
    /// survivor, <see cref="RelationChangeType.MergeDiscarded"/> on the row whose value was thrown
    /// away — carrying that value in <c>OldScore</c>/<c>OldNote</c>, so nothing disappears
    /// silently.</para>
    /// </summary>
    private async Task MoveRelationsAsync(Guid survivorId, Guid duplicateId, Guid actorId)
    {
        var moving = await db.Relations.Where(r => r.MsProfileId == duplicateId).ToListAsync();

        foreach (var relation in moving)
        {
            // Captured before any write: the row is deleted below and its entity detached.
            var (userId, score, note) = (relation.ColumbusUserId, relation.Score, relation.Note);

            var clash = await db.Relations
                .SingleOrDefaultAsync(r => r.MsProfileId == survivorId && r.ColumbusUserId == userId);

            if (clash is null)
            {
                // No collision: the value simply moves. Recorded as arriving on the survivor and
                // as leaving the duplicate, so either profile's timeline tells the whole story.
                await writer.UpsertAsync(userId, survivorId, score, note, actorId,
                    changeType: RelationChangeType.MergeMoved);
                await writer.RemoveAsync(userId, duplicateId, actorId,
                    changeType: RelationChangeType.MergeMoved);
            }
            else if (score > clash.Score)
            {
                // The duplicate's value is stronger and replaces the survivor's. The history row
                // written on the survivor carries the displaced value in OldScore/OldNote and the
                // winner in NewScore/NewNote — one row that names both sides of the collision.
                await writer.UpsertAsync(userId, survivorId, score, note, actorId,
                    changeType: RelationChangeType.MergeDiscarded);
                await writer.RemoveAsync(userId, duplicateId, actorId,
                    changeType: RelationChangeType.MergeMoved);
            }
            else
            {
                // The survivor's value stands (higher, or an equal-score tie). The survivor's row
                // is not written at all — nothing about it changed — and the duplicate's value is
                // preserved in the MergeDiscarded row that removes it.
                await writer.RemoveAsync(userId, duplicateId, actorId,
                    changeType: RelationChangeType.MergeDiscarded);
            }
        }
    }

    /// <summary>
    /// Unions the duplicate's domain links onto the survivor. The link table is keyed on
    /// (ms_profile_id, domain_id), so a domain both profiles already carry is skipped rather than
    /// inserted twice; the duplicate's own links are cleared because a tombstone should not keep
    /// surfacing in domain-filtered queries.
    /// </summary>
    private async Task UnionDomainsAsync(Guid survivorId, Guid duplicateId)
    {
        var alreadyLinked = await db.MsProfileDomains
            .Where(x => x.MsProfileId == survivorId).Select(x => x.DomainId).ToListAsync();
        var moving = await db.MsProfileDomains
            .Where(x => x.MsProfileId == duplicateId).ToListAsync();

        foreach (var link in moving)
        {
            if (!alreadyLinked.Contains(link.DomainId))
                db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = survivorId, DomainId = link.DomainId });
            db.MsProfileDomains.Remove(link);
        }
    }

    /// <summary>
    /// Fills gaps on the survivor from the duplicate and never overwrites anything already known:
    /// the operator picked this record as the better one, so a merge may only add to it.
    /// </summary>
    private static void FillBlanks(MsProfile survivor, MsProfile duplicate)
    {
        if (string.IsNullOrWhiteSpace(survivor.Email)) survivor.Email = duplicate.Email;
        if (string.IsNullOrWhiteSpace(survivor.Title)) survivor.Title = duplicate.Title;
        if (string.IsNullOrWhiteSpace(survivor.Notes)) survivor.Notes = duplicate.Notes;
        survivor.GroupId ??= duplicate.GroupId;
        survivor.SourceId ??= duplicate.SourceId;
        survivor.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
