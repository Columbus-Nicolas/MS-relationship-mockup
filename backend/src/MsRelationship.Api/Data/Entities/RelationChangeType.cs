namespace MsRelationship.Api.Data.Entities;

/// Which shape a change to a Relation took: a row appearing for the first time,
/// an existing row's score or note changing, a row disappearing, or a merge
/// superseding one (FR-32).
///
/// `Merged` is its own member rather than another `Updated` because this table
/// is what Stage 2's undo will be built from, and the two need undoing
/// differently: an `Updated` row is somebody re-assessing a relationship, a
/// `Merged` row is two records of the same person being reconciled and nobody
/// changing their mind at all. Folding them together would make a merge
/// indistinguishable from a human edit in the one place that exists to tell
/// them apart. The column is unconstrained `text`, so a new member needs no
/// migration.
///
/// RelationWriter and MsProfileMerger are the only code that sets this.
public enum RelationChangeType { Created, Updated, Removed, Merged }
