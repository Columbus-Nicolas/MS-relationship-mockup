using System.Text.Json.Serialization;

namespace MsRelationship.Api.Data.Entities;

/// How often the owner is reminded to reach out. None is the default: a person
/// only enters the reminder loop when somebody deliberately puts them there,
/// or the monthly mail becomes a list nobody reads (FR-46).
///
/// Over the wire these are the mockup's own words — 'none', 'monthly',
/// 'quarterly', 'half', 'yearly' (CADENCES in index.html). The camelCase policy
/// in Program.cs gets four of the five right on its own; HalfYearly would go out
/// as "halfYearly", so it says what it is called instead. The stored text stays
/// "HalfYearly", which is the more readable of the two in a database nobody is
/// reading through the API.
public enum ReminderCadence
{
    None,
    Monthly,
    Quarterly,
    [JsonStringEnumMemberName("half")] HalfYearly,
    Yearly
}
