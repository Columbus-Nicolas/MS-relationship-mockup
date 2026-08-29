using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MatchRequest(string Name, string? Email, string Organization = "Microsoft");
public record CreateMsProfileRequest(
    string Name, string Title, string? Email, string Organization,
    Guid? GroupId, Guid? SourceId, string Notes, Guid[] DomainIds);

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
}
