using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ok"));
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
