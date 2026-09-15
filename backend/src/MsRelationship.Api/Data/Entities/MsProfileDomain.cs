namespace MsRelationship.Api.Data.Entities;

/// Membership runs through domains, so belonging to two boards needs no second
/// field to keep in sync (FR-24).
public class MsProfileDomain
{
    public Guid MsProfileId { get; set; }
    public Guid DomainId { get; set; }
}
