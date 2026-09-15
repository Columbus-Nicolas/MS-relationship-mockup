using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Auth;

/// Development only. Reads X-Dev-User and resolves it to a Columbus user, so
/// there is an author on every change before Entra exists. Mapped only when
/// DEV_AUTH is on, so it cannot be reached by accident in any other environment.
public class DevUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, CurrentUser current)
    {
        var email = ctx.Request.Headers["X-Dev-User"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await db.ColumbusUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user is not null) { current.Id = user.Id; current.Email = user.Email; }
        }
        await next(ctx);
    }
}
