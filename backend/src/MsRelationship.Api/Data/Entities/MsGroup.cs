namespace MsRelationship.Api.Data.Entities;

/// The kind of role a Microsoft person holds. A row, not an enum: adding one
/// must never need a migration (TEC-07).
public class MsGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
