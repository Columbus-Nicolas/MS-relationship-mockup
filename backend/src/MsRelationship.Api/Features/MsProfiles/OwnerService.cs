using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public class OwnerService(AppDbContext db)
{
    /// An owner must already hold a relation to the person (FR-43). Owning
    /// somebody nobody has spoken to would be a title, not a job — so a profile
    /// nobody knows simply has no owner, and shows up as the gap it is.
    public async Task<bool> SetOwnerAsync(Guid msProfileId, Guid? ownerId)
    {
        var profile = await db.MsProfiles.FindAsync(msProfileId);
        if (profile is null) return false;

        if (ownerId is not null)
        {
            var holdsRelation = await db.Relations
                .AnyAsync(r => r.MsProfileId == msProfileId && r.ColumbusUserId == ownerId);
            if (!holdsRelation) return false;
        }

        profile.OwnerId = ownerId;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }
}
