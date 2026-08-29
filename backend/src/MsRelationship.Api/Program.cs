using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Features.Relations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<RelationWriter>();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ok"));
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
