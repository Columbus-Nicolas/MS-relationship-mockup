namespace MsRelationship.Api.Data.Entities;

/// One line per time somebody reached out. Date and who wrote it down, and
/// nothing else — the point is only to know when contact last happened (FR-44).
public class ContactEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MsProfileId { get; set; }
    public Guid RegisteredByUserId { get; set; }
    public DateOnly ContactedOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
