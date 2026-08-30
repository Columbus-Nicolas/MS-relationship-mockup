using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Hits the real HTTP pipeline (Kestrel's TestServer, via <see cref="WebApplicationFactory{T}"/>)
/// for every one of the six endpoints migrated off <c>[FromQuery] Guid actorId</c> in Task 13.
/// This is the one thing the pure-unit <see cref="AuthPipelineTests"/> cannot prove: that MVC's
/// binding-source inference actually resolves an <c>ICurrentUser</c> action parameter to the
/// scoped DI service (rather than, say, trying to read it from the request body and colliding
/// with the real <c>[FromBody]</c> parameter, which would be a startup-time
/// <see cref="InvalidOperationException"/> on every affected action). <see cref="TestAuthHandler"/>
/// replaces the real JwtBearer handler so no live Entra tenant or signed token is needed —
/// requests "authenticate" via plain headers instead of a bearer token.
/// </summary>
[Collection("postgres")]
public class ControllerCurrentUserBindingTests(PostgresFixture fixture) : IAsyncLifetime
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

    [Fact]
    public async Task Archive_binds_ICurrentUser_from_the_authenticated_caller()
    {
        await using var db = fixture.NewContext();
        var admin = await Seed.UserAsync(db);
        admin.Role = UserRole.Admin;
        var target = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        var response = await ClientAs(admin).PostAsync($"/api/columbus-users/{target.Id}/archive", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(UserStatus.Archived, (await verify.ColumbusUsers.SingleAsync(u => u.Id == target.Id)).Status);
    }

    [Fact]
    public async Task Archive_is_forbidden_for_a_standard_role_caller()
    {
        await using var db = fixture.NewContext();
        var standard = await Seed.UserAsync(db); // default role is Standard
        var target = await Seed.UserAsync(db);

        var response = await ClientAs(standard).PostAsync($"/api/columbus-users/{target.Id}/archive", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Archive_requires_authentication()
    {
        await using var db = fixture.NewContext();
        var target = await Seed.UserAsync(db);

        var response = await ClientAs(null).PostAsync($"/api/columbus-users/{target.Id}/archive", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Merge_binds_ICurrentUser_and_requires_CanEdit()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var admin = await Seed.UserAsync(db);
        admin.Role = UserRole.Admin;
        await db.SaveChangesAsync();

        var response = await ClientAs(admin).PostAsJsonAsync(
            $"/api/ms-profiles/{survivor.Id}/merge", new { MergeIds = new[] { duplicate.Id } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(survivor.Id, (await verify.MsProfiles.SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Submit_binds_ICurrentUser_as_the_submitter_and_needs_only_authentication()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db); // Standard role — anyone registered may submit
        var profile = await Seed.ProfileAsync(db);

        var request = new SubmitRequest([new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "hi")]);
        var response = await ClientAs(user).PostAsJsonAsync("/api/submissions", request);

        response.EnsureSuccessStatusCode();
        var submission = await response.Content.ReadFromJsonAsync<Submission>();
        Assert.Equal(user.Id, submission!.ColumbusUserId);
    }

    [Fact]
    public async Task Approve_binds_ICurrentUser_as_the_deciding_admin_and_requires_CanEdit()
    {
        await using var db = fixture.NewContext();
        var submitter = await Seed.UserAsync(db);
        var profile = await Seed.ProfileAsync(db);
        var submission = await new SubmissionService(db, new RelationWriter(db))
            .SubmitAsync(submitter.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "note")]);
        var admin = await Seed.UserAsync(db);
        admin.Role = UserRole.Admin;
        await db.SaveChangesAsync();

        var response = await ClientAs(admin).PostAsync($"/api/submissions/{submission.Id}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(admin.Id, (await verify.Submissions.SingleAsync(s => s.Id == submission.Id)).DecidedByUserId);
    }

    // Fix round 1, Critical 1: reject is the other half of the same moderation decision as
    // approve — RejectAsync itself has no role or ownership check (only Status != Pending), so
    // the endpoint-level CanEdit gate is the only thing standing between a Standard user and
    // permanently vetoing anyone's pending submission. This test used to seed a Standard decider
    // and assert 204 (i.e. it pinned the hole as correct); it now asserts the opposite.
    [Fact]
    public async Task Reject_is_forbidden_for_a_standard_role_caller()
    {
        await using var db = fixture.NewContext();
        var submitter = await Seed.UserAsync(db);
        var profile = await Seed.ProfileAsync(db);
        var submission = await new SubmissionService(db, new RelationWriter(db))
            .SubmitAsync(submitter.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "note")]);
        var decider = await Seed.UserAsync(db); // default role is Standard

        var response = await ClientAs(decider).PostAsync($"/api/submissions/{submission.Id}/reject", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(SubmissionStatus.Pending, (await verify.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Reject_binds_ICurrentUser_as_the_deciding_admin_and_requires_CanEdit()
    {
        await using var db = fixture.NewContext();
        var submitter = await Seed.UserAsync(db);
        var profile = await Seed.ProfileAsync(db);
        var submission = await new SubmissionService(db, new RelationWriter(db))
            .SubmitAsync(submitter.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "note")]);
        var admin = await Seed.UserAsync(db);
        admin.Role = UserRole.Admin;
        await db.SaveChangesAsync();

        var response = await ClientAs(admin).PostAsync($"/api/submissions/{submission.Id}/reject", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = fixture.NewContext();
        var stored = await verify.Submissions.SingleAsync(s => s.Id == submission.Id);
        Assert.Equal(SubmissionStatus.Rejected, stored.Status);
        Assert.Equal(admin.Id, stored.DecidedByUserId);
    }

    [Fact]
    public async Task PromoteSuperAdmin_binds_ICurrentUser_as_the_outgoing_holder()
    {
        await using var db = fixture.NewContext();
        var superAdmin = await Seed.UserAsync(db);
        superAdmin.Role = UserRole.SuperAdmin;
        var incoming = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        var response = await ClientAs(superAdmin)
            .PostAsync($"/api/columbus-users/{incoming.Id}/promote-super-admin", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(UserRole.SuperAdmin, (await verify.ColumbusUsers.SingleAsync(u => u.Id == incoming.Id)).Role);
        Assert.Equal(UserRole.Admin, (await verify.ColumbusUsers.SingleAsync(u => u.Id == superAdmin.Id)).Role);
    }

    [Fact]
    public async Task PromoteSuperAdmin_requires_authentication()
    {
        await using var db = fixture.NewContext();
        var incoming = await Seed.UserAsync(db);

        var response = await ClientAs(null).PostAsync($"/api/columbus-users/{incoming.Id}/promote-super-admin", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Fix round 1, Important 2: promote-super-admin now carries [Authorize(Policy =
    // "CanAdminister")]. SuperAdminTransfer.TransferAsync already refuses a non-holder caller on
    // its own (an InvalidOperationException, surfaced as 500) — this proves the endpoint refuses
    // it earlier and more cleanly, with a 403 before the service is ever called.
    [Fact]
    public async Task PromoteSuperAdmin_is_forbidden_for_a_standard_role_caller()
    {
        await using var db = fixture.NewContext();
        var standard = await Seed.UserAsync(db); // default role is Standard
        var incoming = await Seed.UserAsync(db);

        var response = await ClientAs(standard).PostAsync($"/api/columbus-users/{incoming.Id}/promote-super-admin", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verify = fixture.NewContext();
        Assert.Equal(UserRole.Standard, (await verify.ColumbusUsers.SingleAsync(u => u.Id == incoming.Id)).Role);
    }
}
