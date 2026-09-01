using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MsRelationship.Api.Auth;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Fix round 1, Important 5: a Production deployment with a placeholder Entra registration must
/// fail loudly at startup rather than boot clean and let <c>/health</c> report healthy while
/// every real request fails token validation. These are pure unit tests of the guard function —
/// no Postgres, no host, no live IDP needed.
/// </summary>
public class EntraCredentialsGuardTests
{
    private class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration ConfigWithClientId(string? clientId) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:ClientId"] = clientId })
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(EntraCredentialsGuard.PlaceholderClientId)]
    public void Refuses_to_start_outside_development_with_a_missing_or_placeholder_client_id(string? clientId)
    {
        Assert.Throws<InvalidOperationException>(() =>
            EntraCredentialsGuard.Validate(ConfigWithClientId(clientId), new FakeEnvironment("Production")));
    }

    [Fact]
    public void Admits_a_real_looking_client_id_outside_development()
    {
        EntraCredentialsGuard.Validate(
            ConfigWithClientId("11111111-2222-3333-4444-555555555555"), new FakeEnvironment("Production"));
        // No throw is the assertion.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(EntraCredentialsGuard.PlaceholderClientId)]
    public void Development_stays_bootable_regardless_of_client_id(string? clientId)
    {
        EntraCredentialsGuard.Validate(ConfigWithClientId(clientId), new FakeEnvironment("Development"));
        // No throw is the assertion — local runs and the test suite never configure a real tenant.
    }

    /// <summary>
    /// Not just the isolated guard function — the real Program.cs pipeline, with the actual
    /// shipped appsettings.json placeholder, forced into Production. Confirms the guard is
    /// actually wired into startup (call it after <c>builder.Build()</c> and it would be dead
    /// code with green unit tests above and no effect on the running app).
    /// </summary>
    [Fact]
    public void The_real_app_refuses_to_start_in_production_with_the_shipped_placeholder()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Pinned off so the refusal proved here is unambiguously this guard's, and not
            // DevModeGuard tripping first on a stray flag.
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DevModeGuard.ConfigKey] = "false"
                }));
        });

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }
}
