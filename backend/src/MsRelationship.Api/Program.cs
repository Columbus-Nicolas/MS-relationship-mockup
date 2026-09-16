using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Seed;
using MsRelationship.Api.Features.Contacts;
using MsRelationship.Api.Features.Dashboards;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

var builder = WebApplication.CreateBuilder(args);

/* Enums go out as the words the mockup speaks, not as integers. Without this,
   AddControllers() serialised a profile's cadence as `"cadence": 0` — while the
   database deliberately stores 'None' as text so it stays readable, and the
   mockup says 'none' | 'monthly' | 'quarterly' | 'half' | 'yearly'. Three
   vocabularies for one field, and no test looked at it.

   The camelCase policy alone does not reach the mockup's wording everywhere:
   HalfYearly would go out as "halfYearly" and SuperAdmin as "superAdmin". Both
   carry a JsonStringEnumMemberName saying what the mockup actually calls them,
   which takes precedence over the policy. */
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<MsProfileMatcher>();
builder.Services.AddScoped<MsProfileMerger>();
builder.Services.AddScoped<RelationWriter>();
builder.Services.AddScoped<OwnerService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

var devAuth = builder.Configuration.GetValue<bool>("DEV_AUTH");

var app = builder.Build();
app.MapControllers();
app.MapGet("/health", () => Results.Text("ok"));

if (devAuth)
{
    app.UseMiddleware<DevUserMiddleware>();
    app.MapGet("/api/dev/whoami", (ICurrentUser me) =>
        me.IsSignedIn ? Results.Ok(new { me.Id, me.Email }) : Results.Unauthorized());
}

if (builder.Configuration.GetValue("SEED_MOCKUP", false))
{
    using var scope = app.Services.CreateScope();
    await new MockupSeeder(scope.ServiceProvider.GetRequiredService<AppDbContext>()).SeedAsync();
}

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real composition root in tests.</summary>
public partial class Program { }
