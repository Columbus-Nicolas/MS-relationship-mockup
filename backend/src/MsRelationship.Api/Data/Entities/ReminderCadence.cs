namespace MsRelationship.Api.Data.Entities;

/// How often the owner is reminded to reach out. None is the default: a person
/// only enters the reminder loop when somebody deliberately puts them there,
/// or the monthly mail becomes a list nobody reads (FR-46).
public enum ReminderCadence { None, Monthly, Quarterly, HalfYearly, Yearly }
