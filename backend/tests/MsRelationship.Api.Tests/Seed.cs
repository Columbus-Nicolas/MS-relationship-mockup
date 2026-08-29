using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

public static class Seed
{
    public static async Task<ColumbusUser> UserAsync(AppDbContext db)
    {
        var user = new ColumbusUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@columbusglobal.com",
            Name = "Test User"
        };
        db.ColumbusUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<MsProfile> ProfileAsync(AppDbContext db, string? name = null)
    {
        var profile = new MsProfile
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"MS Person {Guid.NewGuid():N}",
            Organization = "Microsoft"
        };
        db.MsProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public static async Task<(ColumbusUser, MsProfile)> PairAsync(AppDbContext db) =>
        (await UserAsync(db), await ProfileAsync(db));
}
