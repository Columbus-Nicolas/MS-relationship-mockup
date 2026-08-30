using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

// legacyId carries the approved mockup's own id (d1, m1, c1, r1, ...) through the JSON so the
// mapping from mockup record to seeded row stays checkable by inspection. It is not persisted —
// no schema change was needed to seed this dataset — so after seeding, checking the mapping
// means matching seed.json entries to database rows by their (unique) Name/Email, the same way
// this file's own cross-references (domainLegacyIds, columbusLegacyId, msProfileLegacyId) work.
public record SeedDomain(string LegacyId, string Name, string Owner, string Desc, bool System = false);

public record SeedProfile(string LegacyId, string Name, string Title, string? Email,
    string Group, string Source, string[] DomainLegacyIds, string Notes, bool Tentative, string? UpdatedAt);

public record SeedColumbusUser(string LegacyId, string Name, string Title, string Department,
    string[] Skills, string Role, string Email, string? LastSurvey);

public record SeedRelation(string LegacyId, string ColumbusLegacyId, string MsProfileLegacyId,
    int Score, string Note, string? UpdatedAt);

public record SeedFile(
    SeedDomain[] Domains, SeedProfile[] MsProfiles, SeedColumbusUser[] ColumbusUsers, SeedRelation[] Relations);

/// <summary>
/// Loads the dataset the approved UI mockup (<c>index.html</c>) was designed against, from
/// <c>Data/seed.json</c> — extracted verbatim from the mockup's <c>state.domains</c>,
/// <c>state.msProfiles</c>, <c>state.columbusProfiles</c> and <c>state.relations</c> arrays.
/// Idempotent: a second call is a no-op, guarded by the presence of any <see cref="MsProfile"/>.
/// Every row — taxonomies, profiles, users and relations — is staged and then persisted in a
/// single <see cref="AppDbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> call,
/// which EF wraps in one implicit transaction, so the guard's premise ("no profiles means
/// nothing has been seeded yet") always holds: there is no intermediate state where taxonomy
/// rows exist but <see cref="MsProfile"/> rows do not, so a crash or restart mid-seed can never
/// leave the guard unable to tell a fresh database from a half-seeded one (fix round 1, finding 1).
/// </summary>
public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.MsProfiles.AnyAsync()) return;

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var domainIds = new Dictionary<string, Guid>();
        foreach (var (domain, index) in seed.Domains.Select((d, i) => (d, i)))
        {
            var id = Guid.NewGuid();
            domainIds[domain.LegacyId] = id;
            db.MsDomains.Add(new MsDomain
            {
                Id = id, Name = domain.Name, Owner = domain.Owner,
                Description = domain.Desc, IsSystem = domain.System, SortOrder = index
            });
        }

        var groupIds = new Dictionary<string, Guid>();
        foreach (var name in seed.MsProfiles.Select(p => p.Group).Distinct())
        {
            var id = Guid.NewGuid();
            groupIds[name] = id;
            db.MsGroups.Add(new MsGroup { Id = id, Name = name });
        }

        var sourceIds = new Dictionary<string, Guid>();
        foreach (var name in seed.MsProfiles.Select(p => p.Source).Distinct())
        {
            var id = Guid.NewGuid();
            sourceIds[name] = id;
            db.MsSources.Add(new MsSource { Id = id, Name = name });
        }

        var departmentIds = new Dictionary<string, Guid>();
        foreach (var name in seed.ColumbusUsers.Select(u => u.Department).Distinct())
        {
            var id = Guid.NewGuid();
            departmentIds[name] = id;
            db.CbDepartments.Add(new CbDepartment { Id = id, Name = name });
        }

        var profileIds = new Dictionary<string, Guid>();
        foreach (var profile in seed.MsProfiles)
        {
            var id = Guid.NewGuid();
            profileIds[profile.LegacyId] = id;
            db.MsProfiles.Add(new MsProfile
            {
                Id = id, Name = profile.Name, Title = profile.Title,
                Email = string.IsNullOrWhiteSpace(profile.Email) ? null : profile.Email,
                Organization = "Microsoft",
                GroupId = groupIds[profile.Group], SourceId = sourceIds[profile.Source],
                Notes = profile.Notes, IsTentative = profile.Tentative,
                UpdatedAt = ParseDate(profile.UpdatedAt) ?? DateTimeOffset.UtcNow
            });
            foreach (var legacy in profile.DomainLegacyIds)
                db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = id, DomainId = domainIds[legacy] });
        }

        var columbusUserIds = new Dictionary<string, Guid>();
        foreach (var user in seed.ColumbusUsers)
        {
            var id = Guid.NewGuid();
            columbusUserIds[user.LegacyId] = id;
            db.ColumbusUsers.Add(new ColumbusUser
            {
                Id = id, Name = user.Name, Title = user.Title, Email = user.Email,
                DepartmentId = departmentIds[user.Department], Skills = user.Skills,
                Role = Enum.Parse<UserRole>(user.Role, ignoreCase: true),
                LastSurveyAt = ParseDate(user.LastSurvey)
            });
        }

        // Seeded directly against db.Relations rather than through RelationWriter. RelationWriter
        // exists to pair every Relation mutation with a RelationHistory row recording who made the
        // change and why (FR-09) — that is a record of an application user acting. Seed data has
        // no such actor: it is the mockup's own baseline state, loaded once at bootstrap, not a
        // mutation performed by anyone. Writing it directly avoids fabricating a "Created" history
        // entry attributed to a synthetic actor for data nobody actually created through the app.
        // This relies on Seeder running only at seed time, never as an in-app mutation path — the
        // one exemption to "relation mutations go through RelationWriter" that this file takes.
        foreach (var relation in seed.Relations)
        {
            db.Relations.Add(new Relation
            {
                Id = Guid.NewGuid(),
                ColumbusUserId = columbusUserIds[relation.ColumbusLegacyId],
                MsProfileId = profileIds[relation.MsProfileLegacyId],
                Score = relation.Score,
                Note = relation.Note,
                UpdatedAt = ParseDate(relation.UpdatedAt) ?? DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new DateTimeOffset(DateOnly.Parse(value).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
