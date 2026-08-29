namespace MsRelationship.Api.Data.Entities;

public class Relation
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public int Score { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
