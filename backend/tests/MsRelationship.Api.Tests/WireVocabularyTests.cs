using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

/// The mockup is the reference for every piece of wording, so an enum has to
/// reach a client as the word the mockup uses. AddControllers() with no JSON
/// options sent integers instead — a profile's cadence went out as
/// `"cadence": 0` while the database stored 'None' and the mockup said 'none'.
///
/// These read the options out of the real composition root rather than
/// rebuilding them, so a change to Program.cs that dropped the converter would
/// fail here rather than quietly agreeing with a copy of itself.
public class WireVocabularyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly JsonSerializerOptions _json;

    public WireVocabularyTests(WebApplicationFactory<Program> factory) =>
        _json = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    private string Wire<T>(T value) => JsonSerializer.Serialize(value, _json).Trim('"');

    /// index.html's CADENCES, in its own words.
    [Theory]
    [InlineData(ReminderCadence.None, "none")]
    [InlineData(ReminderCadence.Monthly, "monthly")]
    [InlineData(ReminderCadence.Quarterly, "quarterly")]
    [InlineData(ReminderCadence.HalfYearly, "half")]
    [InlineData(ReminderCadence.Yearly, "yearly")]
    public void A_cadence_goes_out_as_the_word_the_mockup_uses(ReminderCadence cadence, string expected)
        => Assert.Equal(expected, Wire(cadence));

    /// 'superadmin' is one word in the mockup; a bare camelCase policy would
    /// have sent "superAdmin". 'editor' has no mockup counterpart by design —
    /// §3.11 collapsed 'moderator' and 'standard' into it.
    [Theory]
    [InlineData(UserRole.Editor, "editor")]
    [InlineData(UserRole.Admin, "admin")]
    [InlineData(UserRole.SuperAdmin, "superadmin")]
    public void A_role_goes_out_as_the_word_the_mockup_uses(UserRole role, string expected)
        => Assert.Equal(expected, Wire(role));

    [Theory]
    [InlineData(CustomerType.Unknown, "unknown")]
    [InlineData(CustomerType.Customer, "customer")]
    [InlineData(CustomerType.Prospect, "prospect")]
    public void A_customer_type_goes_out_as_the_word_the_seed_file_uses(CustomerType type, string expected)
        => Assert.Equal(expected, Wire(type));

    /// Not a number, whatever else changes. This is the shape of the original
    /// defect, and it would survive a rename of any single member above.
    [Fact]
    public void No_enum_reaches_a_client_as_an_integer()
    {
        Assert.All(
            [Wire(ReminderCadence.None), Wire(UserRole.Editor), Wire(UserStatus.Active),
             Wire(CustomerType.Unknown), Wire(RelationChangeType.Merged)],
            s => Assert.False(int.TryParse(s, out _), $"serialised as an integer: {s}"));
    }
}
