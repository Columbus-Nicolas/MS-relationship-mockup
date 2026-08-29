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

        submission.Status = SubmissionStatus.Approved;
        submission.DecidedByUserId = actorId;
        submission.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RejectAsync(Guid submissionId, Guid actorId)
    {
        var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
        if (submission.Status != SubmissionStatus.Pending)
            throw new InvalidOperationException("This submission has already been decided.");

        submission.Status = SubmissionStatus.Rejected;
        submission.DecidedByUserId = actorId;
        submission.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
