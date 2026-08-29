using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

/// <summary>
/// Archives a leaver instead of deleting them. The user row, their <see cref="Relation"/>
/// rows and their <see cref="RelationHistory"/> are all retained — only <see cref="ColumbusUser.Status"/>
/// and <see cref="ColumbusUser.ArchivedAt"/> change. This does not go through
/// <see cref="Relations.RelationWriter"/> because no relation row is mutated: the score and
/// note are copied unchanged into a <see cref="RelationChangeType.UserArchived"/> history entry
/// purely to mark the moment of archiving on the timeline.
/// </summary>
public class UserArchiver(AppDbContext db)
{
    public async Task ArchiveAsync(Guid userId, Guid actorId)
    {
        var user = await db.ColumbusUsers.SingleAsync(u => u.Id == userId);
        if (user.Role == UserRole.SuperAdmin)
            throw new InvalidOperationException("The Super Admin profile cannot be archived. Transfer the role first.");
        if (user.Status == UserStatus.Archived) return;

        var relations = await db.Relations.Where(r => r.ColumbusUserId == userId).ToListAsync();
        foreach (var relation in relations)
        {
            db.RelationHistory.Add(new RelationHistory
            {
                Id = Guid.NewGuid(),
                ColumbusUserId = userId,
                MsProfileId = relation.MsProfileId,
                OldScore = relation.Score,
                NewScore = relation.Score,
                OldNote = relation.Note,
                NewNote = relation.Note,
                ChangeType = RelationChangeType.UserArchived,
                ChangedByUserId = actorId
            });
        }

        user.Status = UserStatus.Archived;
        user.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
