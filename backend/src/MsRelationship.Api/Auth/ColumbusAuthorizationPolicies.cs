using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

/// <summary>
/// The two named policies plus the fallback, factored out of <c>Program.cs</c> into one place
/// that both the running app and <c>AuthorizationPolicyTests</c> call, so the tests exercise the
/// exact same policy definitions Program.cs registers rather than a hand-copied approximation
/// that could silently drift from it.
/// </summary>
public static class ColumbusAuthorizationPolicies
{
    public static AuthorizationBuilder AddColumbusPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy("CanEdit", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)))
            .AddPolicy("CanAdminister", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
