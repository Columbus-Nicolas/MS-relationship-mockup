namespace MsRelationship.Api.Data.Entities;

/// Columbus customer, prospect, or not yet established. Unknown is the default:
/// the source material colours some names, but too irregularly to read (FR-50).
public enum CustomerType { Unknown, Customer, Prospect }
