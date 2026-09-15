using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MatchResult(MsProfile? Exact, IReadOnlyList<MsProfile> Suspected);

public class MsProfileMatcher(AppDbContext db)
{
    /// Surveys and imports let several people enter the same Microsoft person
    /// independently, with different spellings and often no e-mail. An exact key
    /// returns the existing row; a same-name-different-key is reported as
    /// suspected, for a human to resolve — never merged silently (FR-19).
    public async Task<MatchResult> MatchAsync(string name, string? email, string? organization)
    {
        var key = IdentityKey.For(email, name, organization);
        var exact = await db.MsProfiles
            .FirstOrDefaultAsync(p => p.IdentityKey == key && p.MergedIntoId == null);

        var normalised = IdentityKey.Normalize(name);
        var suspected = await db.MsProfiles
            .Where(p => p.MergedIntoId == null && p.IdentityKey != key && p.Name.ToLower() == normalised)
            .ToListAsync();

        return new MatchResult(exact, suspected);
    }
}
