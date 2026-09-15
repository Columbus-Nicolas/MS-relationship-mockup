namespace MsRelationship.Api.Data.Entities;

/// Which of the three shapes a change to a Relation took: a row appearing for
/// the first time, an existing row's score or note changing, or a row
/// disappearing. RelationWriter is the only code that sets this (FR-32).
public enum RelationChangeType { Created, Updated, Removed }
