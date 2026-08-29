namespace MsRelationship.Api.Data.Entities;

public class RelationHistory
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public int? OldScore { get; set; }
    public int? NewScore { get; set; }
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public RelationChangeType ChangeType { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? SubmissionId { get; set; }
}
