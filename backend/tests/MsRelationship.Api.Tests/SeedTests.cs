using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Seed;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SeedTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly PostgresFixture _pg;
    private readonly WebApplicationFactory<Program> _factory;

    public SeedTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
    {
        _pg = pg;
        _factory = factory;
    }

    [Fact]
    public async Task The_whole_mockup_data_set_loads()
    {
        // Its own database, not the one every other class in this collection
        // shares: Fixtures.Trio alone adds rows across a dozen other test
        // classes, and these are exact counts.
        await using var db = await _pg.CreateIsolatedDatabaseAsync();
        await new MockupSeeder(db).SeedAsync();

        Assert.Equal(102, await db.MsProfiles.CountAsync());
        Assert.Equal(26, await db.ColumbusUsers.CountAsync());
        Assert.Equal(70, await db.Relations.CountAsync());
        Assert.Equal(70, await db.Customers.CountAsync());
        Assert.Equal(2, await db.Dashboards.CountAsync());
        Assert.Equal(17, await db.Domains.CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = await _pg.CreateIsolatedDatabaseAsync();
        await new MockupSeeder(db).SeedAsync();
        var before = await db.MsProfiles.CountAsync();
        await new MockupSeeder(db).SeedAsync();
        Assert.Equal(before, await db.MsProfiles.CountAsync());
    }

    [Fact]
    public async Task Every_owner_in_the_seed_holds_a_relation_to_the_person_they_own()
    {
        await using var db = await _pg.CreateIsolatedDatabaseAsync();
        await new MockupSeeder(db).SeedAsync();

        var broken = await db.MsProfiles
            .Where(p => p.OwnerId != null)
            .Where(p => !db.Relations.Any(r => r.MsProfileId == p.Id && r.ColumbusUserId == p.OwnerId))
            .Select(p => p.Name)
            .ToListAsync();

        // Holds vacuously here: the mockup itself sets no owner on any of the
        // 102 profiles, so there is nothing to violate FR-43 with. That is the
        // real shape of the source data, not a hole in the test.
        Assert.Empty(broken);
    }

    /// The stage's exit criterion, proved end to end: boot the real API through
    /// its own composition root against a freshly seeded, isolated database, and
    /// read the list back over HTTP exactly as a client would.
    [Fact]
    public async Task The_seeded_API_serves_the_whole_mockup_data_set()
    {
        string connectionString;
        await using (var seedDb = await _pg.CreateIsolatedDatabaseAsync())
            connectionString = seedDb.Database.GetConnectionString()!;

        using var client = _factory.WithWebHostBuilder(b => b
                .UseSetting("ConnectionStrings:Default", connectionString)
                .UseSetting("SEED_MOCKUP", "true"))
            .CreateClient();

        var json = await client.GetStringAsync("/api/ms-profiles");
        var people = JsonNode.Parse(json)!.AsArray();

        Assert.Equal(102, people.Count);
        Assert.All(people, node =>
        {
            var person = node!.AsObject();
            Assert.False(string.IsNullOrWhiteSpace(person["name"]!.GetValue<string>()));

            // Decision 7: the mockup sets no owner and logs no contact for anyone,
            // so both fields must come back present and null — never omitted,
            // never fabricated. Checking ContainsKey as well as the value is the
            // only way to tell "present but null" apart from "absent"; a plain
            // POCO deserialize collapses both to the same default.
            Assert.True(person.ContainsKey("ownerName"));
            Assert.Null(person["ownerName"]);
            Assert.True(person.ContainsKey("lastContact"));
            Assert.Null(person["lastContact"]);

            Assert.True(person.ContainsKey("dashboards"));
            Assert.NotNull(person["dashboards"]);
            Assert.True(person.ContainsKey("scores"));
            Assert.NotNull(person["scores"]);
        });
    }
}
