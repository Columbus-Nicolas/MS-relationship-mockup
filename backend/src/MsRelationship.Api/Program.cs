using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Features.ColumbusUsers;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<RelationWriter>();
builder.Services.AddScoped<MsProfileMatcher>();
builder.Services.AddScoped<MsProfileMerger>();
builder.Services.AddScoped<UserArchiver>();
builder.Services.AddScoped<SubmissionService>();
builder.Services.AddScoped<SuperAdminTransfer>();

// AzureAd:ClientId/TenantId/Instance are deliberately blank in .env — the app registration
// doesn't exist yet (see AZUREAD__* in .env). But Microsoft.Identity.Web validates ClientId
// eagerly (IDW10106) the first time ANY request is authenticated — including a request with no
// bearer token at all, hitting an [AllowAnonymous] endpoint like /health — because
// UseAuthentication() below always runs the authentication handler to populate context.User,
// regardless of endpoint metadata. An empty ClientId would 500 every single request, so
// appsettings.json carries syntactically-valid (non-secret) placeholders for Instance/TenantId/
// ClientId. Real values, once the registration exists, go in the AZUREAD__* environment
// variables and take precedence over these placeholders via normal ASP.NET Core config layering.
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

// ColumbusUser.Role in the database is the source of truth for these policies, not any Entra
// token claim (no such claim exists). MembershipMiddleware.InvokeAsync resolves the
// authenticated principal to its ColumbusUser row and then calls
// context.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Role, ...)])) to stamp the
// database role onto the principal as a *role claim* before authorization runs. RequireRole
// below matches against role claims across every identity on the ClaimsPrincipal, so without
// that AddIdentity call these policies would have no role claim to ever match against — a
// database role and a claims-only policy would be a silent authorization hole where every
// admin endpoint 403s looking like a misconfiguration rather than "nobody wired the role in".
//
// SetFallbackPolicy requires *some* authenticated principal for every endpoint that carries no
// [Authorize] attribute of its own (merge/archive/approve below opt into the stricter CanEdit
// role check instead). Without it, list/match/submit endpoints would be reachable by anyone on
// the network with no token at all, which defeats NFR-02's closed membership list — the
// point of this task is that the whole API is closed, not just the three mutating actions named
// in the brief. /health is carved out explicitly via .AllowAnonymous() below.
builder.Services.AddAuthorizationBuilder().AddColumbusPolicies();

var app = builder.Build();
app.UseAuthentication();
app.UseMiddleware<MembershipMiddleware>();
app.UseAuthorization();
app.MapGet("/health", () => Results.Text("ok")).AllowAnonymous();
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
