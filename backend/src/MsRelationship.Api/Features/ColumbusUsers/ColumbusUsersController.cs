using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

[ApiController]
[Route("api/columbus-users")]
public class ColumbusUsersController(AppDbContext db, UserArchiver archiver) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ColumbusUser>> List([FromQuery] bool includeArchived = false) =>
        await db.ColumbusUsers
            .Where(u => includeArchived || u.Status == UserStatus.Active)
            .OrderBy(u => u.Name)
            .ToListAsync();

    // ICurrentUser arrives in Task 13. Until then, the actor is a route-supplied query
    // parameter; Task 13 Step 6 replaces it with ICurrentUser.Id.
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, [FromQuery] Guid actorId)
    {
        await archiver.ArchiveAsync(id, actorId);
        return NoContent();
    }
}
