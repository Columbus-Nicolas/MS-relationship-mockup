namespace MsRelationship.Api.Data.Entities;

/// Where the information about a Microsoft profile came from — LinkedIn, an
/// org chart, a colleague's list. A row, not an enum: adding one must never
/// need a migration (TEC-07).
public class MsSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
