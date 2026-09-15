namespace MsRelationship.Api.Data.Entities;

/// A Columbus employee who knows people at Microsoft — the Columbus side of a
/// relationship, and who a contact-log entry or a profile's owner points to.
public class ColumbusUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Title { get; set; }
    public Guid? DepartmentId { get; set; }
    public CbDepartment? Department { get; set; }
    /// Unknown for a good share of today's Columbus users. Unique when present;
    /// several users may each have none.
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.Editor;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string[] Skills { get; set; } = [];
}
