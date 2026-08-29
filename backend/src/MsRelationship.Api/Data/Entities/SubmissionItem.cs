namespace MsRelationship.Api.Data.Entities;

public class SubmissionItem
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid MsProfileId { get; set; }
    public SubmissionAction Action { get; set; }
    public int? NewScore { get; set; }
    public string? NewNote { get; set; }
    public int? PrevScore { get; set; }
    public string? PrevNote { get; set; }
}
