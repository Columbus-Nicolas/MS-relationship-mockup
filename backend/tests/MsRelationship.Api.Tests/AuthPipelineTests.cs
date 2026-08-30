using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Exercises <see cref="MembershipMiddleware"/> end to end (domain gate, closed list, archived
/// users, Entra-object-id binding, role stamping) and the named policies from
/// <see cref="ColumbusAuthorizationPolicies"/>, all without a live Entra tenant or a real JWT.
/// <see cref="MembershipMiddleware"/> only ever sees an already-authenticated
/// <see cref="ClaimsPrincipal"/> (it runs after <c>UseAuthentication()</c>), so these tests hand
/// it one built by hand with the same claim types a real Entra ID token carries
/// ("oid", "preferred_username") — this is exactly what a live IDP would have produced by the
/// time the middleware runs, without needing one.
/// </summary>
[Collection("postgres")]
public class AuthPipelineTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static IConfiguration Config(string allowedDomains = "columbusglobal.com") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:AllowedEmailDomains"] = allowedDomains
            })
            .Build();

    private class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private static HttpContext AuthenticatedContext(string authType, params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authType);
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static HttpContext AnonymousContext() => new DefaultHttpContext();

    private async Task<(HttpContext context, bool nextCalled, CurrentUser currentUser)> InvokeAsync(
        HttpContext context, IConfiguration config, bool isDevelopment = false)
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        await using var db = fixture.NewContext();
        var currentUser = new CurrentUser();
        var middleware = new MembershipMiddleware(next, new FakeEnvironment(isDevelopment ? "Development" : "Production"), config);
        await middleware.InvokeAsync(context, db, currentUser);
        return (context, nextCalled, currentUser);
    }

    [Fact]
    public async Task Unauthenticated_requests_pass_through_untouched()
    {
        var (context, nextCalled, _) = await InvokeAsync(AnonymousContext(), Config());

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_registered_active_user_is_admitted_and_bound_with_their_role()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        user.Role = UserRole.Admin;
        await db.SaveChangesAsync();

        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-1"),
            new Claim("preferred_username", user.Email));

        var (ctx, nextCalled, currentUser) = await InvokeAsync(context, Config());

        Assert.True(nextCalled);
        Assert.Equal(user.Id, currentUser.Id);
        Assert.Equal(UserRole.Admin, currentUser.Role);
        Assert.Equal(user.Email, currentUser.Email);
        Assert.True(ctx.User.IsInRole(nameof(UserRole.Admin)));
    }

    [Fact]
    public async Task First_sign_in_binds_the_entra_object_id_to_the_pre_created_row()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        Assert.Null(user.EntraObjectId);

        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-2"),
            new Claim("preferred_username", user.Email));

        await InvokeAsync(context, Config());

        await using var verify = fixture.NewContext();
        var stored = await verify.ColumbusUsers.FindAsync(user.Id);
        Assert.Equal("entra-oid-2", stored!.EntraObjectId);
    }

    [Fact]
    public async Task An_archived_user_cannot_authenticate()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        user.Status = UserStatus.Archived;
        user.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-3"),
            new Claim("preferred_username", user.Email));

        var (ctx, nextCalled, currentUser) = await InvokeAsync(context, Config());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        Assert.Equal(Guid.Empty, currentUser.Id);
    }

    [Fact]
    public async Task A_valid_token_for_someone_not_on_the_closed_list_is_refused()
    {
        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-unknown"),
            new Claim("preferred_username", "nobody@columbusglobal.com"));

        var (ctx, nextCalled, _) = await InvokeAsync(context, Config());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task An_email_outside_the_allowed_domain_is_refused_even_if_a_matching_row_exists()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db); // seeded with a columbusglobal.com email
        user.Email = "someone@gmail.com";
        await db.SaveChangesAsync();

        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-4"),
            new Claim("preferred_username", user.Email));

        var (ctx, nextCalled, _) = await InvokeAsync(context, Config());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task An_empty_allow_list_is_deny_all_outside_development_even_through_the_full_pipeline()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-5"),
            new Claim("preferred_username", user.Email));

        var (ctx, nextCalled, _) = await InvokeAsync(context, Config(allowedDomains: ""), isDevelopment: false);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task An_empty_allow_list_permits_a_registered_user_in_development_only()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var context = AuthenticatedContext("Bearer",
            new Claim("oid", "entra-oid-6"),
            new Claim("preferred_username", user.Email));

        var (ctx, nextCalled, currentUser) = await InvokeAsync(context, Config(allowedDomains: ""), isDevelopment: true);

        Assert.True(nextCalled);
        Assert.Equal(user.Id, currentUser.Id);
    }

    // -- Policy tests: same AddColumbusPolicies() call Program.cs registers, no live IDP needed. --

    private static IServiceProvider PolicyServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddColumbusPolicies();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal PrincipalWithRole(UserRole role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], "Bearer"));

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    [InlineData(UserRole.Moderator, false)]
    [InlineData(UserRole.Standard, false)]
    public async Task CanEdit_admits_only_admin_and_super_admin(UserRole role, bool expected)
    {
        var authorization = PolicyServices().GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(PrincipalWithRole(role), "CanEdit");

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    [InlineData(UserRole.Moderator, false)]
    [InlineData(UserRole.Standard, false)]
    public async Task CanAdminister_admits_only_admin_and_super_admin(UserRole role, bool expected)
    {
        var authorization = PolicyServices().GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(PrincipalWithRole(role), "CanAdminister");

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task The_fallback_policy_requires_only_an_authenticated_user()
    {
        // This is the policy AuthorizationMiddleware applies to any endpoint with no [Authorize]
        // attribute of its own (Submit, Reject, PromoteSuperAdmin, the list/match endpoints) — it
        // is what keeps the rest of the API from being reachable with no token at all.
        var services = PolicyServices();
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var fallbackPolicy = await policyProvider.GetFallbackPolicyAsync();
        Assert.NotNull(fallbackPolicy);

        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type => not authenticated
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(UserRole.Standard))], "Bearer"));

        var denied = await authorization.AuthorizeAsync(unauthenticated, resource: null, fallbackPolicy!);
        var admitted = await authorization.AuthorizeAsync(authenticated, resource: null, fallbackPolicy!);

        Assert.False(denied.Succeeded);
        Assert.True(admitted.Succeeded);
    }
}
