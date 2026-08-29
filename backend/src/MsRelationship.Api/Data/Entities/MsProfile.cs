namespace MsRelationship.Api.Data.Entities;

public class MsProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Email { get; set; }
    public string Organization { get; set; } = "Microsoft";
    public Guid? GroupId { get; set; }
    public Guid? SourceId { get; set; }
    public string Notes { get; set; } = "";
    public bool IsTentative { get; set; }
    public Guid? MergedIntoId { get; set; }
    public string IdentityKey { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
