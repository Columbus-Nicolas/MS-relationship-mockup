namespace MsRelationship.Api.Data.Entities;

/// Which Microsoft people touch which customers. A profile and a customer
/// each gain more rows here rather than a list field on either side, which is
/// what makes the backwards query in FR-49 possible in the first place.
public class MsProfileCustomer
{
    public Guid MsProfileId { get; set; }
    public Guid CustomerId { get; set; }
}
