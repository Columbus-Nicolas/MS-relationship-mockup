using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

public class CurrentUser : ICurrentUser
{
    public Guid Id { get; private set; }
    public UserRole Role { get; private set; }
    public string Email { get; private set; } = "";

    public void Bind(ColumbusUser user)
    {
        Id = user.Id;
        Role = user.Role;
        Email = user.Email;
    }
}
