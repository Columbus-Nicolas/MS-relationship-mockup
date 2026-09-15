using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Data.Entities;

/// One person at Microsoft. The row that stops the same person being entered
/// twice across import rounds: `IdentityKey` is unique, so a later sighting of
/// Anne Berg either matches this row (Task 7) or is rejected, never duplicated
/// (FR-18).
public class MsProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Title { get; set; }
    /// Empty until a real source fills it. Never generated or guessed (FR-29).
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Organization { get; set; }
    public required string IdentityKey { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? SourceId { get; set; }
    public string? Notes { get; set; }
    /// Carried over from a source that could not settle something about them.
    public bool IsTentative { get; set; }
    public ReminderCadence Cadence { get; set; } = ReminderCadence.None;
    public Guid? OwnerId { get; set; }
    /// Set when this profile has been merged away; see Task 13.
    public Guid? MergedIntoId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static MsProfile Create(string name, string? email, string? organization) => new()
    {
        Name = name,
        Email = email,
        Organization = organization,
        IdentityKey = Features.MsProfiles.IdentityKey.For(email, name, organization)
    };
}
