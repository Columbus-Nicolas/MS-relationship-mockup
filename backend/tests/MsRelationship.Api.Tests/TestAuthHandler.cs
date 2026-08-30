using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Stands in for the real Entra JWT bearer handler in HTTP-level tests, so the full
/// authentication -&gt; MembershipMiddleware -&gt; authorization pipeline can be exercised end to
/// end without a live IDP or a real signed token. A request "authenticates" by sending the
/// caller's claims as plain headers (<c>X-Test-Oid</c>, <c>X-Test-Email</c>) instead of a
/// bearer token; with neither header the request is treated as anonymous, exactly like a real
/// caller with no token at all.
/// </summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Email", out var email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("preferred_username", email.ToString()) };
        if (Request.Headers.TryGetValue("X-Test-Oid", out var oid))
            claims.Add(new Claim("oid", oid.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
