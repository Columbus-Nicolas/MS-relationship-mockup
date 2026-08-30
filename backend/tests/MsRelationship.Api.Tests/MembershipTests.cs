using MsRelationship.Api.Auth;
using Xunit;

namespace MsRelationship.Api.Tests;

public class MembershipTests
{
    [Theory]
    [InlineData("someone@columbusglobal.com", true)]
    [InlineData("someone@COLUMBUSGLOBAL.COM", true)]
    [InlineData("someone@gmail.com", false)]
    [InlineData("", false)]
    public void Allowed_domains_are_matched_case_insensitively(string email, bool expected) =>
        Assert.Equal(expected, MembershipMiddleware.IsAllowedDomain(email, ["columbusglobal.com"], isDevelopment: false));

    [Fact]
    public void An_empty_allow_list_permits_anyone_in_development_only()
    {
        Assert.True(MembershipMiddleware.IsAllowedDomain("anyone@example.com", [], isDevelopment: true));
        Assert.False(MembershipMiddleware.IsAllowedDomain("anyone@example.com", [], isDevelopment: false));
    }
}
