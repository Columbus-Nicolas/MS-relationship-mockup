using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MsRelationship.Api.Auth;
using Xunit;

namespace MsRelationship.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        // DEV_MODE is pinned off rather than left to default: EnvFile only reads .env when the
        // process environment says Development, which a test run does not, but pinning it here
        // means the suite cannot be swayed by an untracked local file under any circumstance.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DevModeGuard.ConfigKey] = "false"
                })));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
