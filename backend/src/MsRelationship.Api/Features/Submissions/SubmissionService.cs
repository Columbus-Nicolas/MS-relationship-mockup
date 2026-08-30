using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Features.Submissions;

public record SubmissionDraft(Guid MsProfileId, SubmissionAction Action, int? Score, string? Note);

/// <summary>
/// Implements the submit-then-approve workflow (FR-07). Submitting only records a proposal —
/// it never touches <see cref="Relation"/> rows. Approving is the only path that mutates
/// relations, and it does so exclusively through <see cref="RelationWriter"/> (never
/// <see cref="AppDbContext.Relations"/> directly), tagging every resulting
/// <see cref="RelationHistory"/> row with the submission id so the change is traceable.
/// All items in a submission are applied atomically: if any item fails, the whole
/// approval rolls back and the submission stays Pending.
/// </summary>
public class SubmissionService(AppDbContext db, RelationWriter writer)
{
    public async Task<Submission> SubmitAsync(Guid columbusUserId, IReadOnlyList<SubmissionDraft> drafts)
    {
        var submission = new Submission { Id = Guid.NewGuid(), ColumbusUserId = columbusUserId };
        db.Submissions.Add(submission);

        foreach (var draft in drafts)
        {
            var current = await db.Relations.SingleOrDefaultAsync(r =>
                r.ColumbusUserId == columbusUserId && r.MsProfileId == draft.MsProfileId);

            db.SubmissionItems.Add(new SubmissionItem
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                MsProfileId = draft.MsProfileId,
                Action = draft.Action,
                NewScore = draft.Score,
                NewNote = draft.Note,
                PrevScore = current?.Score,
                PrevNote = current?.Note
            });
        }

        await db.SaveChangesAsync();
        return submission;
    }

    public async Task ApproveAsync(Guid submissionId, Guid actorId)
    {
        var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
        if (submission.Status != SubmissionStatus.Pending)
            throw new InvalidOperationException("This submission has already been decided.");

        var items = await db.SubmissionItems.Where(i => i.SubmissionId == submissionId).ToListAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var item in items)
        {
            if (item.Action == SubmissionAction.Remove)
                // Intentional no-op when the relation is already gone (e.g. two pending
                // submissions targeted the same profile, or it was removed independently
                // between submit and approve): RelationWriter.RemoveAsync silently returns
                // without writing history in that case. The desired end state (no relation)
                // already holds, and this SubmissionItem row remains the durable record of
                // what was requested and approved — do not make this throw or force a
                // history write for a change that has no effect.
                await writer.RemoveAsync(submission.ColumbusUserId, item.MsProfileId, actorId, submissionId);
            else
                await writer.UpsertAsync(submission.ColumbusUserId, item.MsProfileId,
                    item.NewScore ?? 0, item.NewNote ?? "", actorId, submissionId);
        }

        // Conditional write, not a plain tracked assignment: Submission carries no concurrency
        // token (a schema change, out of scope for this fix), so the WHERE clause re-asserts
        // "still Pending" at write time — the same idiom MsProfileMerger's tombstone write and
        // SuperAdminTransfer's role updates use. Without this, two admins clearing a moderation
        // backlog could both pass the guard above (a plain read) — approve-vs-approve is mostly
        // caught by accident (Relation.UpdatedAt's token or the unique pair index), but
        // approve-vs-reject was protected by nothing: a reject issued after this approve had
        // already committed would silently flip the submission to Rejected while its relation
        // writes stayed live. Proven deterministically (no threads/sleeps) by
        // <see cref="SubmissionTests.Approve_then_reject_do_not_both_succeed"/>, which mirrors
        // <see cref="SuperAdminTransferTests.Two_concurrent_transfers_from_the_same_holder_do_not_both_succeed"/>.
        var decidedAt = DateTimeOffset.UtcNow;
        var approved = await db.Submissions
            .Where(s => s.Id == submissionId && s.Status == SubmissionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubmissionStatus.Approved)
                .SetProperty(x => x.DecidedByUserId, actorId)
                .SetProperty(x => x.DecidedAt, decidedAt));
        Detach(submissionId);
        if (approved != 1)
            throw new InvalidOperationException("This submission has already been decided.");

        await tx.CommitAsync();
    }

    public async Task RejectAsync(Guid submissionId, Guid actorId)
    {
        var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
        if (submission.Status != SubmissionStatus.Pending)
            throw new InvalidOperationException("This submission has already been decided.");

        // See the matching comment in ApproveAsync: the same conditional-write idiom, closing
        // the same race for the other half of the moderation decision. A single ExecuteUpdateAsync
        // statement is already atomic, so no explicit transaction is needed here.
        var decidedAt = DateTimeOffset.UtcNow;
        var rejected = await db.Submissions
            .Where(s => s.Id == submissionId && s.Status == SubmissionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubmissionStatus.Rejected)
                .SetProperty(x => x.DecidedByUserId, actorId)
                .SetProperty(x => x.DecidedAt, decidedAt));
        Detach(submissionId);
        if (rejected != 1)
            throw new InvalidOperationException("This submission has already been decided.");
    }

    /// <summary>
    /// <c>ExecuteUpdateAsync</c> writes straight to the database and bypasses the change
    /// tracker, so the tracked <see cref="Submission"/> instance loaded at the top of
    /// <see cref="ApproveAsync"/>/<see cref="RejectAsync"/> would otherwise keep showing its
    /// pre-decision <c>Status</c> for the rest of this <see cref="AppDbContext"/>'s lifetime —
    /// including a later call in the same scope, which would then read the stale status instead
    /// of the real one. Detaching forces the next query for this id back to the database.
    /// Mirrors <see cref="MsRelationship.Api.Features.ColumbusUsers.SuperAdminTransfer.Detach"/>.
    /// </summary>
    private void Detach(Guid id)
    {
        var entry = db.ChangeTracker.Entries<Submission>().FirstOrDefault(e => e.Entity.Id == id);
        if (entry is not null) entry.State = EntityState.Detached;
    }
}
