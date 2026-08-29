namespace MsRelationship.Api.Data.Entities;

public class MsDomain
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
}
