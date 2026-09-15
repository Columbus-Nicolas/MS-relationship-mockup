using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.Dashboards;

public record DeleteResult(bool Deleted, bool HasDomains, bool IsSystem);

public class DashboardService(AppDbContext db)
{
    /// Deleting a dashboard must not be able to orphan a domain, a profile or a
    /// relationship, so an occupied board is refused rather than cascaded (FR-26).
    public async Task<DeleteResult> DeleteAsync(Guid id)
    {
        var board = await db.Dashboards.FindAsync(id);
        if (board is null) return new DeleteResult(false, false, false);
        if (board.IsSystem) return new DeleteResult(false, false, true);

        if (await db.Domains.AnyAsync(d => d.DashboardId == id))
            return new DeleteResult(false, true, false);

        db.Dashboards.Remove(board);
        await db.SaveChangesAsync();
        return new DeleteResult(true, false, false);
    }
}
