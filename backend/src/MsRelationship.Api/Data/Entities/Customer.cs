namespace MsRelationship.Api.Data.Entities;

/// A company Columbus sells to or targets — customer or prospect. A record of
/// its own rather than a text field on a profile, so "who at Microsoft
/// touches Novo?" is a question the system can answer by querying backwards
/// (FR-49).
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public CustomerType Type { get; set; } = CustomerType.Unknown;
}
