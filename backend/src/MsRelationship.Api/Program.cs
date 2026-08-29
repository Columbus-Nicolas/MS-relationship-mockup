var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ok"));
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
