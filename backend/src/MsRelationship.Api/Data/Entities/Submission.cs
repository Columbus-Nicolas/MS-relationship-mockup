namespace MsRelationship.Api.Data.Entities;

public class Submission
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
