namespace MsRelationship.Api.Data.Entities;

/// Append-only. One row per change, with the value before and after, so
/// "who changed this, when, and what did it say before" is answerable without
/// reading application logs (NFR-07). Nothing updates or deletes these rows.
public class RelationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public short? OldScore { get; set; }
    public short? NewScore { get; set; }
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public RelationChangeType ChangeType { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
