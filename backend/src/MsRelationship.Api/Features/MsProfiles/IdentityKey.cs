using System.Text.RegularExpressions;

namespace MsRelationship.Api.Features.MsProfiles;

/// One canonical key per Microsoft person, stored rather than derived at query
/// time (FR-18). Work e-mail where it is known; name + organisation otherwise,
/// because an Anne Berg at Microsoft Norway is not the Danish one.
public static partial class IdentityKey
{
    public static string For(string? email, string name, string? organization)
    {
        if (!string.IsNullOrWhiteSpace(email)) return Squash(email);
        return $"{Squash(name)}|{Squash(organization ?? "")}";
    }

    private static string Squash(string s) => Whitespace().Replace(s.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
