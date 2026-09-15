namespace MsRelationship.Api.Data.Entities;

/// A category of Microsoft people, and a panel on its dashboard. Stored apart
/// from the people so Microsoft can reorganise without touching relationships (FR-05).
public class Domain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// Null for the system "Unmarked" domain, which belongs to no dashboard.
    public Guid? DashboardId { get; set; }
    public Dashboard? Dashboard { get; set; }
    public required string Name { get; set; }
    public string? Owner { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
}
