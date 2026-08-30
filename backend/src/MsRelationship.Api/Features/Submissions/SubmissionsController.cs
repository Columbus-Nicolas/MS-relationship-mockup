using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Submissions;

public record SubmitRequest(SubmissionDraft[] Items);

[ApiController]
[Route("api/submissions")]
public class SubmissionsController(AppDbContext db, SubmissionService service) : ControllerBase
{
    // The moderation queue exposes every user's proposed scores and notes, not just the caller's
    // own — without a policy, any authenticated user (including Standard) could read the whole
    // backlog. Gated to match the write side (approve/reject below): leaving the read half open
    // while the write half is gated for exactly this reason is the same asymmetry that caused
    // the original problem (Fix round 1, Critical 1, on Reject).
    [Authorize(Policy = "CanEdit")]
    [HttpGet]
    public async Task<IReadOnlyList<Submission>> Pending() =>
        await db.Submissions.Where(s => s.Status == SubmissionStatus.Pending)
            .OrderBy(s => s.SubmittedAt).ToListAsync();

    // Any authenticated, registered Columbus user may submit a proposal (FR-07's
    // submit-then-approve workflow) — gated by the fallback authenticated-user policy only,
    // not CanEdit.
    [HttpPost]
    public async Task<Submission> Submit(ICurrentUser currentUser, [FromBody] SubmitRequest request) =>
        await service.SubmitAsync(currentUser.Id, request.Items);

    [Authorize(Policy = "CanEdit")]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, ICurrentUser currentUser)
    {
        await service.ApproveAsync(id, currentUser.Id);
        return NoContent();
    }

    // Approve and reject are two halves of one moderation decision on the same submission —
    // both require CanEdit. A Standard or Moderator caller must not be able to permanently
    // veto another user's pending proposal (RejectAsync's guard is only Status != Pending, with
    // no role or ownership check of its own).
    [Authorize(Policy = "CanEdit")]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, ICurrentUser currentUser)
    {
        await service.RejectAsync(id, currentUser.Id);
        return NoContent();
    }
}
