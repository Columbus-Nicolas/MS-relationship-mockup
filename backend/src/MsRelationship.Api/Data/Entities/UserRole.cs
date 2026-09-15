namespace MsRelationship.Api.Data.Entities;

/// Roles collapsed to two plus an owner when editing became open (§3.11):
/// everybody can edit, so a role only says who may administer.
public enum UserRole { Editor, Admin, SuperAdmin }
