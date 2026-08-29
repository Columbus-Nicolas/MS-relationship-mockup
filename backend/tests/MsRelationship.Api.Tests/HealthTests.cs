using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MsRelationship.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
