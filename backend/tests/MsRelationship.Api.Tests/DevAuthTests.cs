using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Dev mode end to end through the real pipeline: the <c>X-Dev-Email</c> header, the boot-time
/// migrate-and-seed, <see cref="MembershipMiddleware"/>'s closed-list check and the database role
/// being stamped onto the principal — none of it stubbed.
///
/// The half that matters most is the negative one. Every assertion that dev mode <em>works</em>
/// is also a description of an authentication bypass, so the tests that prove it is absent when
/// the flag is off carry the actual safety guarantee.
/// </summary>
[Collection("postgres")]
public class DevAuthTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable(DevModeGuard.ConfigKey, null);
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The flag goes into the process environment rather than through
    /// <c>ConfigureAppConfiguration</c>, because Program.cs has to read it before the service
    /// container is built and configuration added by the test factory does not exist yet at that
    /// point. That is not a workaround so much as the real path: .env is loaded into exactly this
    /// place on a developer's machine. Getting it wrong is not silent — DevModeGuard refuses to
    /// start and says so, which is what
    /// <see cref="DevModeGuardTests.The_real_app_refuses_to_start_when_the_flag_arrives_too_late"/>
    /// pins down.
    ///
    /// A process variable is global to the test run, which is why the assembly disables test
    /// parallelisation (see AssemblyInfo.cs).
    /// </summary>
    private HttpClient Client(bool devMode)
    {
        Environment.SetEnvironmentVariable(DevModeGuard.ConfigKey, devMode ? "true" : "false");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = fixture.ConnectionString,
                    // Empty means allow-any-domain in Development, which is what the seeded
                    // @columbusglobal.example addresses need. Set explicitly so the test does not
                    // silently depend on whatever appsettings.json happens to carry.
                    ["AzureAd:AllowedEmailDomains"] = ""
                })));

        return _factory.CreateClient();   // the host is built here, with the variable in place
    }

    private static HttpRequestMessage As(string email, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(DevAuthHandler.EmailHeader, email);
        return request;
    }

    [Fact]
    public async Task Booting_in_dev_mode_migrates_and_seeds_the_database()
    {
        var client = Client(devMode: true);
        await client.GetAsync("/health");   // forces the host to start

        await using var db = fixture.NewContext();
        Assert.Equal(23, await db.MsProfiles.CountAsync());                  // the approved dataset...
        Assert.Equal(15, await db.ColumbusUsers.CountAsync());               // ...plus the developer
        Assert.True(await db.ColumbusUsers.AnyAsync(u => u.Email == DevSeeder.DevUserEmail));
    }

    [Fact]
    public async Task The_developer_account_can_reach_an_authenticated_endpoint()
    {
        var client = Client(devMode: true);

        var response = await client.SendAsync(As(DevSeeder.DevUserEmail, "/api/columbus-users"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// CanEdit matches on a role claim that only exists because
    /// <see cref="MembershipMiddleware"/> read it out of <c>columbus_users</c> and stamped it onto
    /// the principal. Reaching this endpoint proves the seeded role survives the whole chain —
    /// the dev handler itself issues no role claim at all.
    /// </summary>
    [Fact]
    public async Task The_developer_account_carries_its_database_role_into_authorization()
    {
        var client = Client(devMode: true);

        var response = await client.SendAsync(As(DevSeeder.DevUserEmail, "/api/submissions"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_no_header_is_still_unauthorized()
    {
        var client = Client(devMode: true);

        var response = await client.GetAsync("/api/columbus-users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Membership is not weakened by dev mode: naming an address that is not on the closed list
    /// authenticates but does not get in.
    /// </summary>
    [Fact]
    public async Task An_unregistered_address_is_rejected_even_in_dev_mode()
    {
        var client = Client(devMode: true);

        var response = await client.SendAsync(As("stranger@columbusglobal.example", "/api/columbus-users"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_session_endpoint_reports_the_developer_account()
    {
        var client = Client(devMode: true);

        var response = await client.GetAsync("/api/dev/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DevSessionResponse>();
        Assert.True(body!.Enabled);
        Assert.Equal(DevSeeder.DevUserEmail, body.User.Email);
        Assert.Equal(DevSeeder.DevUserRole.ToString(), body.User.Role);
    }

    /// <summary>
    /// What makes the sign-in invisible rather than merely inert: with the flag off the route is
    /// never mapped, so the login page's probe fails and it renders exactly as shipped.
    ///
    /// The refusal is 401 rather than 404 because a fallback authorization policy is applied to
    /// requests that match no endpoint at all, not just to matched ones — so an unmapped route
    /// answers an anonymous caller the same way a protected one does. Either way the frontend
    /// sees a non-OK response and leaves the page alone; the contrast with
    /// <see cref="The_session_endpoint_reports_the_developer_account"/>, which gets a 200 body
    /// back, is what proves the route exists only in dev mode.
    /// </summary>
    [Fact]
    public async Task The_session_endpoint_is_unreachable_when_dev_mode_is_off()
    {
        var client = Client(devMode: false);

        var response = await client.GetAsync("/api/dev/session");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The one that matters: with the flag off the header is not a way in. The dev scheme is
    /// never registered, so the request is handled by the Entra bearer handler, finds no token,
    /// and is refused like any other anonymous caller.
    /// </summary>
    [Fact]
    public async Task The_dev_header_is_ignored_when_dev_mode_is_off()
    {
        var client = Client(devMode: false);

        var response = await client.SendAsync(As(DevSeeder.DevUserEmail, "/api/columbus-users"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record DevSessionResponse(bool Enabled, DevSessionUser User);
    private record DevSessionUser(Guid Id, string Name, string Email, string Role, string Status);
}
