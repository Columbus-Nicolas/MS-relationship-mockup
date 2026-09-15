using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class DevUserTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly PostgresFixture _pg;
    private readonly WebApplicationFactory<Program> _factory;

    public DevUserTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
    {
        _pg = pg;
        _factory = factory;
    }

    private WebApplicationFactory<Program> Factory(PostgresFixture pg) =>
        _factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", pg.ConnectionString)
                                          .UseSetting("DEV_AUTH", "true"));

    [Fact]
    public async Task Without_the_header_nobody_is_signed_in()
    {
        var res = await Factory(_pg).CreateClient().GetAsync("/api/dev/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_header_names_the_acting_user()
    {
        await using var db = _pg.NewContext();
        if (!await db.ColumbusUsers.AnyAsync(u => u.Email == "seeded@columbusglobal.example"))
        {
            db.ColumbusUsers.Add(new ColumbusUser { Name = "Seeded", Email = "seeded@columbusglobal.example" });
            await db.SaveChangesAsync();
        }

        var client = Factory(_pg).CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "seeded@columbusglobal.example");
        var res = await client.GetAsync("/api/dev/whoami");
        res.EnsureSuccessStatusCode();
        Assert.Contains("seeded@columbusglobal.example", await res.Content.ReadAsStringAsync());
    }
}
