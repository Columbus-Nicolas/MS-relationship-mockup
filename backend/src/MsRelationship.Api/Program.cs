using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Features.Dashboards;
using MsRelationship.Api.Features.MsProfiles;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<MsProfileMatcher>();

var app = builder.Build();
app.MapControllers();
app.MapGet("/health", () => Results.Text("ok"));
app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real composition root in tests.</summary>
public partial class Program { }
