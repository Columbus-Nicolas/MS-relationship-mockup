using System.Net;
using Microsoft.AspNetCore.Hosting;
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

    /// No DEV_AUTH setting at all — only the environment name says "Development".
    /// Proves the flag no longer follows that label (Program.cs reads DEV_AUTH
    /// alone, with no environment-based fallback).
    private WebApplicationFactory<Program> FactoryWithoutDevAuth(PostgresFixture pg) =>
        _factory.WithWebHostBuilder(b => b.UseEnvironment("Development")
                                          .UseSetting("ConnectionStrings:Default", pg.ConnectionString));

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

    [Fact]
    public async Task Without_DEV_AUTH_the_route_does_not_exist_even_in_Development()
    {
        var res = await FactoryWithoutDevAuth(_pg).CreateClient().GetAsync("/api/dev/whoami");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
