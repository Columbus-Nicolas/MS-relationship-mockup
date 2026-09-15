using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Seed;
using MsRelationship.Api.Features.Contacts;
using MsRelationship.Api.Features.Dashboards;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
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
