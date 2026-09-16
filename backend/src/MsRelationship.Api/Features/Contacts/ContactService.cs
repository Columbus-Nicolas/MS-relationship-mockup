using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Contacts;

public class ContactService(AppDbContext db, ICurrentUser me)
{
    /// Anyone signed in may register a contact, not only the owner: it is a fact
    /// about the relationship, not the owner's property (FR-44).
    ///
    /// Null when there is no such person to have contacted — no row, or one
    /// merged away. Like RelationWriter.SetAsync, and for the same reason: a
    /// contact logged against a tombstone would never surface, because the merge
    /// that would have moved it to the survivor has already run. Nothing else
    /// here queries MsProfiles, so the global query filter does not reach this
    /// path on its own.
    public async Task<ContactEntry?> RegisterAsync(Guid msProfileId, DateOnly? on = null)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A contact needs somebody to attribute it to.");
        if (!await db.MsProfiles.AnyAsync(p => p.Id == msProfileId)) return null;

        var entry = new ContactEntry
        {
            MsProfileId = msProfileId,
            RegisteredByUserId = actor,
            ContactedOn = on ?? DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.ContactEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    /// Null when nothing is logged. Null means Unknown, not Never: an empty log
    /// means nobody wrote it down, not that nobody called (FR-45).
    public Task<ContactEntry?> LastContactAsync(Guid msProfileId) =>
        db.ContactEntries.Where(c => c.MsProfileId == msProfileId)
            .OrderByDescending(c => c.ContactedOn).ThenByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
}
