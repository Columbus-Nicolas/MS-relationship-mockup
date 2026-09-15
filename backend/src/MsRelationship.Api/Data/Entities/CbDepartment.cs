namespace MsRelationship.Api.Data.Entities;

/// The Columbus department a Columbus employee belongs to. A row, not an
/// enum: adding one must never need a migration (TEC-07).
public class CbDepartment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
