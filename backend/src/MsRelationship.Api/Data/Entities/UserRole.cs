using System.Text.Json.Serialization;

namespace MsRelationship.Api.Data.Entities;

/// Roles collapsed to two plus an owner when editing became open (§3.11):
/// everybody can edit, so a role only says who may administer.
///
/// The mockup writes the top role as one word, 'superadmin', which the camelCase
/// policy in Program.cs would render "superAdmin" — so that member says what it
/// is called. 'editor' has no mockup counterpart on purpose: §3.11 collapsed the
/// mockup's 'moderator' and 'standard' into it.
public enum UserRole { Editor, Admin, [JsonStringEnumMemberName("superadmin")] SuperAdmin }
