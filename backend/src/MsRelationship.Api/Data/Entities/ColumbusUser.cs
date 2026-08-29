namespace MsRelationship.Api.Data.Entities;

public class ColumbusUser
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid? DepartmentId { get; set; }
    public string[] Skills { get; set; } = [];
    public UserRole Role { get; set; } = UserRole.Standard;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? LastSurveyAt { get; set; }
}
