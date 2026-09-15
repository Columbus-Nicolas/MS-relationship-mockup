namespace MsRelationship.Api.Data.Entities;

/// One Columbus person's view of one Microsoft person. The score is the whole
/// scale: -3 to +3, and nothing else (FR-01).
public class Relation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public short Score { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
