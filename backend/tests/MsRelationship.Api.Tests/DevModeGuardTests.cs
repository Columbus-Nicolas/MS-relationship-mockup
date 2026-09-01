using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MsRelationship.Api.Auth;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// Dev mode replaces Entra authentication with a plain request header, so the only acceptable
/// behaviour outside Development is a refusal to boot. These mirror
/// <see cref="EntraCredentialsGuardTests"/> deliberately — same structure, opposite direction:
/// that guard fails when real credentials are missing, this one fails when a local shortcut is
/// present somewhere it must not be.
/// </summary>
public class DevModeGuardTests
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

    private static IConfiguration Config(string? devMode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevModeGuard.ConfigKey] = devMode })
            .Build();

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", true)]
    public void Refuses_to_start_when_dev_mode_is_enabled_outside_development(string environment, bool registered)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DevModeGuard.Validate(Config("true"), new FakeEnvironment(environment), registered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    public void Admits_a_non_development_environment_when_the_flag_is_not_set(string? devMode)
    {
        DevModeGuard.Validate(Config(devMode), new FakeEnvironment("Production"), registeredDevMode: false);
        // No throw is the assertion.
    }

    [Fact]
    public void Development_may_enable_dev_mode()
    {
        DevModeGuard.Validate(Config("true"), new FakeEnvironment("Development"), registeredDevMode: true);
        // No throw is the assertion — this is the one environment the mode exists for.
    }

    /// <summary>
    /// The flag is set where the safety check can see it but arrived too late for the
    /// registration decision, so dev mode is not actually active. Silently continuing would
    /// present as every request being refused for no visible reason.
    /// </summary>
    [Fact]
    public void Refuses_to_start_when_the_flag_arrived_too_late_to_take_effect()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DevModeGuard.Validate(Config("true"), new FakeEnvironment("Development"), registeredDevMode: false));

        Assert.Contains("not visible when the application registered its services", ex.Message);
    }

    /// <summary>The opposite skew: the bypass is registered but the flag has since vanished.</summary>
    [Fact]
    public void Refuses_to_start_when_dev_mode_was_registered_but_the_flag_is_gone()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DevModeGuard.Validate(Config("false"), new FakeEnvironment("Development"), registeredDevMode: true));
    }

    /// <summary>
    /// .env leaves unset values blank by convention, so a blank flag has to mean "off" rather
    /// than crash the host on a failed boolean conversion.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("off")]
    public void A_blank_or_falsey_flag_reads_as_off(string? devMode)
    {
        Assert.False(DevModeGuard.IsEnabled(Config(devMode), new FakeEnvironment("Development")));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public void The_common_spellings_of_on_are_all_accepted(string devMode)
    {
        Assert.True(DevModeGuard.IsEnabled(Config(devMode), new FakeEnvironment("Development")));
    }

    /// <summary>
    /// A value that plainly states an intention must not be silently ignored — the symptom would
    /// be unexplained 401s against a config file that says dev mode is on.
    /// </summary>
    [Fact]
    public void An_unrecognised_value_is_an_error_rather_than_a_quiet_false()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DevModeGuard.IsEnabled(Config("enabled"), new FakeEnvironment("Development")));

        Assert.Contains("not a recognised boolean", ex.Message);
    }

    [Theory]
    [InlineData("true", "Development", true)]
    [InlineData("true", "Production", false)]   // unreachable past Validate; proves the second gate stands alone
    [InlineData("false", "Development", false)]
    [InlineData(null, "Development", false)]
    public void IsEnabled_requires_both_the_flag_and_development(string? devMode, string environment, bool expected)
    {
        Assert.Equal(expected, DevModeGuard.IsEnabled(Config(devMode), new FakeEnvironment(environment)));
    }

    /// <summary>
    /// Not the isolated guard function but the real <c>Program.cs</c> pipeline, proving the call
    /// is actually wired into startup. Remove the call and every unit test above stays green while
    /// the running app loses the protection entirely.
    /// </summary>
    [Fact]
    public void The_real_app_refuses_to_start_in_production_with_dev_mode_on()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DevModeGuard.ConfigKey] = "true",
                    // A real-looking client id, so the only reason to refuse is dev mode itself
                    // rather than EntraCredentialsGuard tripping first.
                    ["AzureAd:ClientId"] = "11111111-2222-3333-4444-555555555555"
                }));
        });

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }

    /// <summary>
    /// The same wiring check for the late-configuration case, and incidentally a demonstration of
    /// why <see cref="DevAuthTests"/> sets a process environment variable instead of using
    /// <c>ConfigureAppConfiguration</c> to switch dev mode on.
    /// </summary>
    [Fact]
    public void The_real_app_refuses_to_start_when_the_flag_arrives_too_late()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DevModeGuard.ConfigKey] = "true"
                })));

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }
}
