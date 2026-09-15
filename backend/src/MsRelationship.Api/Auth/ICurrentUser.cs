namespace MsRelationship.Api.Auth;

/// Who is acting. Every write records this (FR-32). Stage 1 fills it from a
/// header; Entra fills it from a token later. Nothing above this line changes.
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Email { get; }
    bool IsSignedIn { get; }
}

public class CurrentUser : ICurrentUser
{
    public Guid? Id { get; set; }
    public string? Email { get; set; }
    public bool IsSignedIn => Id is not null;
}
