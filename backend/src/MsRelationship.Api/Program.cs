using Microsoft.EntityFrameworkCore;
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
builder.Services.AddScoped<UserArchiver>();
builder.Services.AddScoped<SubmissionService>();
builder.Services.AddScoped<SuperAdminTransfer>();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ok"));
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
