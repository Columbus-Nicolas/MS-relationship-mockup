using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

public static class Fixtures
{
    /// An actor, a Columbus person and a Microsoft person, all unique per call so
    /// tests sharing one container cannot collide on a unique index.
    public static async Task<(ColumbusUser Actor, ColumbusUser User, MsProfile Profile)> Trio(AppDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var actor = new ColumbusUser { Name = $"Actor {tag}", Email = $"actor-{tag}@columbusglobal.example" };
        var user = new ColumbusUser { Name = $"User {tag}", Email = $"user-{tag}@columbusglobal.example" };
        var profile = MsProfile.Create($"MS {tag}", $"ms-{tag}@microsoft.example", null);
        db.AddRange(actor, user, profile);
        await db.SaveChangesAsync();
        return (actor, user, profile);
    }
}
