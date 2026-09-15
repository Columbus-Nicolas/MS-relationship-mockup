using System.Text.RegularExpressions;

namespace MsRelationship.Api.Features.MsProfiles;

/// One canonical key per Microsoft person, stored rather than derived at query
/// time (FR-18). Work e-mail where it is known; name + organisation otherwise,
/// because an Anne Berg at Microsoft Norway is not the Danish one.
public static partial class IdentityKey
{
    public static string For(string? email, string name, string? organization)
    {
        if (!string.IsNullOrWhiteSpace(email)) return Normalize(email);
        return $"{Normalize(name)}|{Normalize(organization ?? "")}";
    }

    /// Trim, collapse internal whitespace runs to one space, and lowercase —
    /// the form two sightings of the same e-mail, name or organisation both
    /// reduce to. Public so MsProfileMatcher (Task 7) can normalise a search
    /// name the same way before comparing it against a stored one.
    public static string Normalize(string s) => CollapseWhitespace(s).ToLowerInvariant();

    /// The whitespace half of <see cref="Normalize"/> alone, case left as-is —
    /// for a value that must stay display-cased, such as the name
    /// MsProfile.Create stores (Task 7).
    public static string CollapseWhitespace(string s) => Whitespace().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
