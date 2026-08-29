using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Submissions;

public record SubmitRequest(SubmissionDraft[] Items);

// NOTE: actorId is taken as an explicit parameter rather than derived from an authenticated
// principal because ICurrentUser/authentication does not exist yet (Task 13). Task 13 will
// swap these signatures over to pull the actor from the authenticated context.
[ApiController]
[Route("api/submissions")]
public class SubmissionsController(AppDbContext db, SubmissionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Submission>> Pending() =>
        await db.Submissions.Where(s => s.Status == SubmissionStatus.Pending)
            .OrderBy(s => s.SubmittedAt).ToListAsync();

    [HttpPost]
    public async Task<Submission> Submit([FromQuery] Guid actorId, [FromBody] SubmitRequest request) =>
        await service.SubmitAsync(actorId, request.Items);

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromQuery] Guid actorId)
    {
        await service.ApproveAsync(id, actorId);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromQuery] Guid actorId)
    {
        await service.RejectAsync(id, actorId);
        return NoContent();
    }
}
