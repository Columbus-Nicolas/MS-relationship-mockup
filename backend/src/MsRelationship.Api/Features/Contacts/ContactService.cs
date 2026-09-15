using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Contacts;

public class ContactService(AppDbContext db, ICurrentUser me)
{
    /// Anyone signed in may register a contact, not only the owner: it is a fact
    /// about the relationship, not the owner's property (FR-44).
    public async Task<ContactEntry> RegisterAsync(Guid msProfileId, DateOnly? on = null)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A contact needs somebody to attribute it to.");
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
