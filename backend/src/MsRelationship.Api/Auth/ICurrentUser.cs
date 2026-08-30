using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

public interface ICurrentUser
{
    Guid Id { get; }
    UserRole Role { get; }
    string Email { get; }
}
