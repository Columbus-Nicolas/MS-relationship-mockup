using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

[ApiController]
[Route("api/ms-profiles")]
public class MsProfilesController(AppDbContext db) : ControllerBase
{
    /// Raw rows plus the four derived values the list needs: owner name, last
    /// contact date, the dashboards the person's domains belong to, and their
    /// scores. Statistics stay on the client, so the production numbers are the
    /// mockup's arithmetic on the mockup's shapes — see §4.4 of the plan.
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.MsProfiles
        .Where(p => p.MergedIntoId == null)
        .OrderBy(p => p.Name)
        .Select(p => new
        {
            p.Id, p.Name, p.Title, p.Email, p.Phone, p.Cadence, p.IsTentative,
            OwnerName = db.ColumbusUsers.Where(u => u.Id == p.OwnerId).Select(u => u.Name).FirstOrDefault(),
            LastContact = db.ContactEntries.Where(c => c.MsProfileId == p.Id)
                .OrderByDescending(c => c.ContactedOn).Select(c => (DateOnly?)c.ContactedOn).FirstOrDefault(),
            Dashboards = db.MsProfileDomains.Where(x => x.MsProfileId == p.Id)
                .Join(db.Domains, x => x.DomainId, d => d.Id, (x, d) => d.DashboardId)
                .Where(d => d != null)
                .Distinct().ToList(),
            Scores = db.Relations.Where(r => r.MsProfileId == p.Id).Select(r => r.Score).ToList()
        })
        .ToListAsync());
}
