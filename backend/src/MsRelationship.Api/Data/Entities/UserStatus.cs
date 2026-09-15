namespace MsRelationship.Api.Data.Entities;

/// Whether a Columbus user still signs in and owns relationships, or has left
/// and is kept only so their past relations and contact-log entries still
/// have someone to point to.
public enum UserStatus { Active, Archived }
