using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MsRelationship.Api.Auth;

/// <summary>
/// Stands in for the real Entra JWT bearer handler when <c>DEV_MODE</c> is on, so the app can be
/// signed into on a laptop with no app registration, no tenant and no token. A request
/// "authenticates" by naming the caller in an <c>X-Dev-Email</c> header.
///
/// This is the same trick <c>TestAuthHandler</c> plays in the test project, and deliberately so:
/// that handler proves the whole authentication -&gt; <see cref="MembershipMiddleware"/> -&gt;
/// authorization chain works without a live IDP, but it lives in the test assembly where a
/// running app cannot reach it. Rather than a second, subtly different bypass, this emits the
/// exact same two claims a real Entra token carries and the middleware already reads —
/// <c>preferred_username</c> and <c>oid</c> — so dev mode exercises the real pipeline rather than
/// a shortcut around it. Membership still applies: the email must resolve to an Active
/// <see cref="Data.Entities.ColumbusUser"/> row or the request is rejected like any other.
///
/// With no header the result is <see cref="AuthenticateResult.NoResult"/> rather than a failure,
/// which keeps an unauthenticated request genuinely anonymous — <c>/health</c> stays reachable
/// and the fallback policy still returns 401 for everything else, exactly as in production.
///
/// Registration is guarded twice over: <c>Program.cs</c> registers this scheme *instead of*
/// Entra, never alongside it, and only when <see cref="DevModeGuard.IsEnabled"/> agrees. There is
/// no configuration in which both schemes exist, so this cannot become a second way in to a real
/// deployment.
/// </summary>
public class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevScheme";
    public const string EmailHeader = "X-Dev-Email";
    public const string ObjectIdHeader = "X-Dev-Oid";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(EmailHeader, out var email) || string.IsNullOrWhiteSpace(email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("preferred_username", email.ToString()) };
        if (Request.Headers.TryGetValue(ObjectIdHeader, out var oid) && !string.IsNullOrWhiteSpace(oid))
            claims.Add(new Claim("oid", oid.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
