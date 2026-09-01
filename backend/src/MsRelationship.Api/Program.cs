using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Npgsql;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Configuration;
using MsRelationship.Api.Data;
using MsRelationship.Api.Features.ColumbusUsers;
using MsRelationship.Api.Features.Dev;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;

// Must run before CreateBuilder: the builder's environment-variable configuration source reads
// the process environment once, at construction, so anything .env contributes has to already be
// there. See EnvFile for why the file is parsed here instead of being sourced by the shell.
var envFile = EnvFile.Load(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));

var builder = WebApplication.CreateBuilder(args);

// Whether to register the dev authentication scheme has to be settled now, before the service
// container is built. The matching safety check runs after Build() against the final
// configuration — see DevModeGuard for why the flag is deliberately read at both moments.
var devMode = DevModeGuard.IsEnabled(builder.Configuration, builder.Environment);

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

const string DevCorsPolicy = "DevCors";

if (devMode)
{
    // Exactly one authentication scheme is ever registered. Dev mode replaces Entra rather than
    // adding to it, so there is no configuration in which both a header and a bearer token are
    // accepted — the bypass cannot become a second door into a real deployment. It also means
    // the IDW10106 problem described in the else branch simply does not arise here, since
    // Microsoft.Identity.Web is never wired up at all.
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

    // The mockup is opened straight off disk, so its requests carry the opaque "null" origin
    // that file:// documents send. No origin list can match that, hence AllowAnyOrigin. This
    // policy is only ever registered inside this branch.
    builder.Services.AddCors(o => o.AddPolicy(DevCorsPolicy, p => p
        .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
}
else
{
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
}

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

// Loud failure, not a silent one: see EntraCredentialsGuard for why this can't just be assumed.
EntraCredentialsGuard.Validate(app.Configuration, app.Environment);

// The authoritative dev-mode check, against the built app's configuration rather than the
// builder's: this is the view that sees every source, and the one allowed to stop the process.
DevModeGuard.Validate(app.Configuration, app.Environment, devMode);

if (envFile is not null) app.Logger.LogInformation("Loaded environment file {EnvFile}", envFile);

if (devMode)
{
    app.Logger.LogWarning(
        "DEV_MODE is on. Entra authentication is replaced by the {Header} header and a " +
        "developer account is seeded. Local development only.", DevAuthHandler.EmailHeader);

    // Dev mode owns its database end to end: a developer should get a working, populated schema
    // from `dotnet run` alone. Deployed environments deliberately keep migrations a separate,
    // deliberate step rather than something a web process does to itself on boot.
    //
    // Both seeders are idempotent (Seeder guards on any MsProfile existing, DevSeeder on the
    // developer account's email), so this is safe to repeat on every restart.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        await Seeder.SeedAsync(db);
        await DevSeeder.SeedDevUserAsync(db);
    }
    catch (NpgsqlException ex)
    {
        // The overwhelmingly likely cause is that the database container simply is not running —
        // containers stop whenever Docker Desktop restarts or the machine sleeps, so this is a
        // routine occurrence rather than an exotic failure. Left alone it surfaces as an
        // unhandled "Connection refused" stack trace that says nothing about docker compose, so
        // the fix gets rewritten as a sentence. The password is deliberately not echoed back;
        // only the coordinates needed to tell whether they point where you expected.
        var target = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        throw new InvalidOperationException(
            $"Dev mode could not reach Postgres at {target.Host}:{target.Port} " +
            $"(database '{target.Database}', user '{target.Username}'). Start it with " +
            "`docker compose up -d db mail`, then run again. If it is already running, check that " +
            "POSTGRES_PORT and ConnectionStrings__Default in .env agree with each other.", ex);
    }
}

// Before UseAuthentication so preflight requests are answered without being run through the
// authentication handler.
if (devMode) app.UseCors(DevCorsPolicy);

app.UseAuthentication();
app.UseMiddleware<MembershipMiddleware>();
app.UseAuthorization();
app.MapGet("/health", () => Results.Text("ok")).AllowAnonymous();
if (devMode) app.MapDevEndpoints();
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
