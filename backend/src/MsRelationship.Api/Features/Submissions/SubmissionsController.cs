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

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, ICurrentUser currentUser)
    {
        await service.RejectAsync(id, currentUser.Id);
        return NoContent();
    }
}
