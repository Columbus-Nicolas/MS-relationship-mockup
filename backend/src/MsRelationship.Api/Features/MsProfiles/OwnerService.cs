using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public class OwnerService(AppDbContext db)
{
    /// An owner must already hold a relation to the person (FR-43). Owning
    /// somebody nobody has spoken to would be a title, not a job — so a profile
    /// nobody knows simply has no owner, and shows up as the gap it is.
    ///
    /// A profile merged away has no owner either — the merge clears the
    /// duplicate's own owner precisely so nothing points at a tombstone, and
    /// setting one afterwards would undo that. The global query filter on
    /// MsProfile is what excludes it, but only because this is an explicit query:
    /// FindAsync hands back an already-tracked row without querying, and would
    /// slip a tombstone past the filter.
    public async Task<bool> SetOwnerAsync(Guid msProfileId, Guid? ownerId)
    {
        var profile = await db.MsProfiles.FirstOrDefaultAsync(p => p.Id == msProfileId);
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
