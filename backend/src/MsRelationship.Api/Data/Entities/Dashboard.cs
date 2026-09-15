namespace MsRelationship.Api.Data.Entities;

/// One department's view of Microsoft. Data, not code: adding a department is a
/// row, and its two pages follow from it (FR-21, FR-22).
public class Dashboard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// Route-safe and stable. Renaming the label never changes this, so links keep working.
    public required string Slug { get; set; }
    public required string Label { get; set; }
    public string? Subtitle { get; set; }
    public string? Owner { get; set; }
    public string? Version { get; set; }
    public string? UpdatedLabel { get; set; }
    /// The landing dashboard. Cannot be deleted (FR-26).
    public bool IsSystem { get; set; }
}
