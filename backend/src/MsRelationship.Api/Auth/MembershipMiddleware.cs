using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

/// <summary>
/// The closed membership list (NFR-02, FR-10, D-4). Two independent gates, both server-side:
/// the email domain, then the closed list of pre-created <see cref="ColumbusUser"/> rows.
/// Entra ID only proves who someone is; it says nothing about whether Columbus has decided
/// they belong in this app, so a valid token alone must never be enough to pass.
/// </summary>
public class MembershipMiddleware(RequestDelegate next, IWebHostEnvironment env, IConfiguration config)
{
    public static bool IsAllowedDomain(string email, IReadOnlyList<string> allowed, bool isDevelopment)
    {
        if (allowed.Count == 0) return isDevelopment;
        if (string.IsNullOrWhiteSpace(email)) return false;

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;

        var domain = email[(at + 1)..];
        return allowed.Any(a => string.Equals(a.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db, CurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var email = context.User.FindFirstValue("preferred_username")
                    ?? context.User.FindFirstValue(ClaimTypes.Email)
                    ?? "";

        var allowed = (config["AzureAd:AllowedEmailDomains"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!IsAllowedDomain(email, allowed, env.IsDevelopment()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "This account is outside Columbus." });
            return;
        }

        var objectId = context.User.FindFirstValue("oid");
        var user = await db.ColumbusUsers.SingleOrDefaultAsync(u =>
            u.EntraObjectId == objectId || u.Email.ToLower() == email.ToLower());

        // Archived users are never deleted (leavers keep their row per FR-10), so the closed
        // list check must also exclude Status != Active — otherwise an archived leaver whose
        // Entra account is still enabled could sign back in indefinitely.
        if (user is null || user.Status != UserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This account is not registered. An administrator must add you first."
            });
            return;
        }

        // First successful sign-in binds the Entra object id to the pre-created row.
        if (user.EntraObjectId is null && objectId is not null)
        {
            user.EntraObjectId = objectId;
            await db.SaveChangesAsync();
        }

        currentUser.Bind(user);

        // The role lives in columbus_users, not in the Entra token, so stamp it onto the
        // principal here. Without this, RequireRole in the policies below never matches.
        context.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Role, user.Role.ToString())]));

        await next(context);
    }
}
