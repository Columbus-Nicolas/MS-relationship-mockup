using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MatchRequest(string Name, string? Email, string Organization = "Microsoft");
public record CreateMsProfileRequest(
    string Name, string Title, string? Email, string Organization,
    Guid? GroupId, Guid? SourceId, string Notes, Guid[] DomainIds);
public record MergeRequest(Guid[] MergeIds);

[ApiController]
[Route("api/ms-profiles")]
public class MsProfilesController(AppDbContext db, MsProfileMatcher matcher) : ControllerBase
{
    [HttpPost("match")]
    public async Task<IReadOnlyList<ProfileMatch>> Match([FromBody] MatchRequest request) =>
        await matcher.FindAsync(request.Name, request.Email, request.Organization);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMsProfileRequest request)
    {
        var key = MsProfileMatcher.KeyFor(request.Name, request.Email, request.Organization);
        var clash = await db.MsProfiles
            .SingleOrDefaultAsync(p => p.MergedIntoId == null && p.IdentityKey == key);

        if (clash is not null)
            return Conflict(new { message = "A profile with this identity already exists.", existing = clash });

        var profile = new MsProfile
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Title = request.Title,
            Email = request.Email,
            Organization = request.Organization,
            GroupId = request.GroupId,
            SourceId = request.SourceId,
            Notes = request.Notes
        };
        db.MsProfiles.Add(profile);
        foreach (var domainId in request.DomainIds.Distinct())
            db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = profile.Id, DomainId = domainId });

        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Create), new { id = profile.Id }, profile);
    }

    /// <summary>
    /// Folds the listed duplicates into <paramref name="survivorId"/> (FR-20). The whole merge is
    /// atomic — see <see cref="MsProfileMerger"/>.
    /// </summary>
    // ICurrentUser arrives in Task 13. Until then, the actor is a route-supplied query
    // parameter, matching the other controllers; Task 13 Step 6 replaces it with ICurrentUser.Id.
    [HttpPost("{survivorId:guid}/merge")]
    public async Task<IActionResult> Merge(Guid survivorId, [FromQuery] Guid actorId,
        [FromBody] MergeRequest request, [FromServices] MsProfileMerger merger)
    {
        await merger.MergeAsync(survivorId, request.MergeIds, actorId);
        return NoContent();
    }
}
