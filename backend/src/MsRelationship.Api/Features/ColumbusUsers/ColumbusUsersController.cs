using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
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

    [Authorize(Policy = "CanEdit")]
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, ICurrentUser currentUser)
    {
        await archiver.ArchiveAsync(id, currentUser.Id);
        return NoContent();
    }

    // `id` is the incoming Super Admin; the outgoing Super Admin is the caller (currentUser.Id).
    // SuperAdminTransfer.TransferAsync itself requires the caller to already hold Role ==
    // SuperAdmin (see its guard), so this is safe to leave under the same authenticated-user
    // fallback policy as the rest of the API rather than CanEdit/CanAdminister specifically —
    // the brief names only merge, archive and approve for the CanEdit attribute.
    [HttpPost("{id:guid}/promote-super-admin")]
    public async Task<IActionResult> PromoteSuperAdmin(Guid id,
        ICurrentUser currentUser, [FromServices] SuperAdminTransfer transfer)
    {
        await transfer.TransferAsync(currentUser.Id, id);
        return NoContent();
    }
}
