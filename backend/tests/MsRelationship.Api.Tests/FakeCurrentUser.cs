using MsRelationship.Api.Auth;

namespace MsRelationship.Api.Tests;

public class FakeCurrentUser(Guid? id) : ICurrentUser
{
    public Guid? Id { get; } = id;
    public string? Email { get; } = id is null ? null : $"{id}@columbusglobal.example";
    public bool IsSignedIn => Id is not null;
}
