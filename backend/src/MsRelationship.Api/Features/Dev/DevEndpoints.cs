using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.Dev;

/// <summary>
/// The one endpoint dev mode exposes, and the mechanism by which the mockup's login page knows
/// whether to offer a developer sign-in at all.
///
/// <c>index.html</c> is opened straight off disk — there is no build step, so it cannot read
/// <c>.env</c> and cannot be told at build time whether dev mode is on. It has to ask. So the
/// contract is deliberately "does this route exist": the login page requests
/// <c>GET /api/dev/session</c>, renders its developer block on a success and stays exactly as it
/// is today on any failure. With <c>DEV_MODE</c> off this is never mapped, the request 404s, and
/// the UI is unchanged — "visible only in dev mode" falls out of the routing table rather than
/// out of a flag the frontend could get wrong.
///
/// Written as a minimal-API endpoint rather than a controller for that reason. Controllers are
/// discovered by assembly scanning, so a <c>DevController</c> would always exist and would need
/// its own attribute-level guard to 404 — one more thing to get wrong. An endpoint that is only
/// mapped inside <c>if (devMode)</c> simply is not in the routing table otherwise.
///
/// Anonymous by necessity: it is what the login page calls *before* anyone has signed in. It
/// discloses only the local account's name, email and role, all of which are constants in
/// <see cref="DevSeeder"/> in a mode that already trusts an unauthenticated header.
/// </summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dev/session", async (AppDbContext db) =>
        {
            // Read the row back rather than echoing the constants: if the account has been
            // re-roled or archived in the local database, the login page should reflect what is
            // actually there instead of what the seeder originally intended.
            var user = await db.ColumbusUsers
                .Where(u => u.Email == DevSeeder.DevUserEmail)
                .Select(u => new { u.Id, u.Name, u.Email, Role = u.Role.ToString(), Status = u.Status.ToString() })
                .SingleOrDefaultAsync();

            return user is null
                ? Results.NotFound(new { message = "Dev mode is on, but the developer account has not been seeded." })
                : Results.Ok(new { enabled = true, user });
        }).AllowAnonymous();
    }
}
