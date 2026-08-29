using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public record ProfileMatch(Guid Id, string Name, string? Email, double Confidence, string Reason);

/// <summary>
/// Surfaces likely-existing <see cref="Data.Entities.MsProfile"/> rows before a new one is
/// created (FR-19), so a user is warned instead of quietly minting a duplicate. Tombstoned
/// rows (<c>merged_into_id</c> set) are never offered as matches.
/// </summary>
public class MsProfileMatcher(AppDbContext db)
{
    private const double SimilarityFloor = 0.4;

    public static string KeyFor(string name, string? email, string organization) =>
        string.IsNullOrWhiteSpace(email)
            ? $"{name.Trim().ToLowerInvariant()}|{organization.Trim().ToLowerInvariant()}"
            : email.Trim().ToLowerInvariant();

    public async Task<IReadOnlyList<ProfileMatch>> FindAsync(string name, string? email, string organization)
    {
        var key = KeyFor(name, email, organization);
        var live = db.MsProfiles.Where(p => p.MergedIntoId == null);

        var exact = await live.Where(p => p.IdentityKey == key)
            .Select(p => new ProfileMatch(p.Id, p.Name, p.Email, 1.0, "identity"))
            .ToListAsync();

        var similar = await live
            .Where(p => p.IdentityKey != key)
            .Select(p => new { p.Id, p.Name, p.Email, Score = EF.Functions.TrigramsSimilarity(p.Name, name) })
            .Where(x => x.Score >= SimilarityFloor)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToListAsync();

        return [.. exact, .. similar.Select(x => new ProfileMatch(x.Id, x.Name, x.Email, x.Score, "similar-name"))];
    }
}
