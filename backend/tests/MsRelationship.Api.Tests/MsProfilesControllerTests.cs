using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// <c>POST /api/ms-profiles</c> (<see cref="MsProfilesController.Create"/>) had no test at all
/// before this fix — not the happy path, not the 409, not the domain links, not authorization —
/// despite spec §6/FR-19 making the 409-on-collision behaviour part of the Definition of Done.
/// Hits the real HTTP pipeline via <see cref="WebApplicationFactory{T}"/>, following the same
/// <c>ClientAs</c> pattern as <see cref="ControllerCurrentUserBindingTests"/>.
/// </summary>
[Collection("postgres")]
public class MsProfilesControllerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient ClientAs(ColumbusUser? user)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = fixture.ConnectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
            });
        });
        var client = _factory.CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Email", user.Email);
            client.DefaultRequestHeaders.Add("X-Test-Oid", $"oid-{user.Id}");
        }
        return client;
    }

    // <see cref="MsProfilesController.Create"/> returns
    // <c>CreatedAtAction(nameof(Create), new { id = profile.Id }, profile)</c>, whose Location
    // header points link generation at the Create action's own route template — which has no
    // <c>{id}</c> segment. If link generation returned null for that mismatch, MVC would throw
    // InvalidOperationException("No route matches the supplied values") while building the
    // response, and the request would come back as a 500, not a 201. Asserting Created here (not
    // just "not an exception") is what proves that did not happen.
    [Fact]
    public async Task Create_returns_201_and_persists_the_profile()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);

        var request = new CreateMsProfileRequest(
            "Jane Doe", "Partner Manager", "jane.doe@microsoft.com", "Microsoft",
            null, null, "met at Ignite", []);
        var response = await ClientAs(user).PostAsJsonAsync("/api/ms-profiles", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MsProfile>();
        Assert.NotNull(body);
        await using var verify = fixture.NewContext();
        var stored = await verify.MsProfiles.SingleAsync(p => p.Id == body!.Id);
        Assert.Equal("Jane Doe", stored.Name);
        Assert.Equal("jane.doe@microsoft.com", stored.Email);
        Assert.Equal("met at Ignite", stored.Notes);
    }

    // Spec §6/FR-19: "The create endpoint refuses an exact identity_key collision with 409
    // Conflict and the colliding record in the body."
    [Fact]
    public async Task Create_refuses_an_identity_key_collision_with_409_and_the_colliding_record()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var existing = await Seed.ProfileAsync(db, "Existing Person");
        existing.Email = "existing@microsoft.com";
        await db.SaveChangesAsync();

        var request = new CreateMsProfileRequest(
            "A Totally Different Name", "Title", "existing@microsoft.com", "Microsoft",
            null, null, "", []);
        var response = await ClientAs(user).PostAsJsonAsync("/api/ms-profiles", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(existing.Id, body.GetProperty("existing").GetProperty("id").GetGuid());

        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.MsProfiles.CountAsync());
    }

    [Fact]
    public async Task Create_persists_the_requested_domain_links()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var domainA = new MsDomain { Id = Guid.NewGuid(), Name = $"A-{Guid.NewGuid():N}" };
        var domainB = new MsDomain { Id = Guid.NewGuid(), Name = $"B-{Guid.NewGuid():N}" };
        db.MsDomains.AddRange(domainA, domainB);
        await db.SaveChangesAsync();

        var request = new CreateMsProfileRequest(
            "Jane Doe", "Partner Manager", null, "Microsoft",
            null, null, "", [domainA.Id, domainB.Id]);
        var response = await ClientAs(user).PostAsJsonAsync("/api/ms-profiles", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MsProfile>();

        await using var verify = fixture.NewContext();
        var links = await verify.MsProfileDomains.Where(x => x.MsProfileId == body!.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.DomainId == domainA.Id);
        Assert.Contains(links, l => l.DomainId == domainB.Id);
    }

    [Fact]
    public async Task Create_requires_authentication()
    {
        var request = new CreateMsProfileRequest("Jane Doe", "Title", null, "Microsoft", null, null, "", []);
        var response = await ClientAs(null).PostAsJsonAsync("/api/ms-profiles", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
