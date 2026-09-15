using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data.Seed;

/// Loads the mockup data set. Idempotent, and wrapped in one transaction: a
/// half-seeded database is worse than an empty one, because it looks like it
/// worked.
///
/// The 70 relations here are placeholder scores from the mockup, not
/// assessments anyone actually made, so this writes db.Relations directly and
/// produces no RelationHistory rows: inventing history for events that never
/// happened would put fiction in a table that exists precisely so nothing in
/// it can be corrected. For the same reason this never calls RelationWriter —
/// it also requires a signed-in user, and seeding has none to invent.
public class MockupSeeder(AppDbContext db)
{
    private record SeedFile(
        List<SeedCustomer> Customers, List<SeedContact> Contacts, List<SeedBoard> Boards,
        List<SeedDomain> Domains, List<SeedMsProfile> MsProfiles,
        List<SeedCbUser> ColumbusProfiles, List<SeedRelation> Relations);

    private record SeedCustomer(string Id, string Name, string Type);
    private record SeedContact(string Id, string MsProfileId, string ById, string Date);
    private record SeedBoard(string Id, string Label, string? Subtitle, string? Owner, string? Version, string? Updated, bool System);
    private record SeedDomain(string Id, string? Dept, string Name, string? Owner, string? Desc, bool System);
    private record SeedMsProfile(string Id, string Name, string? Title, string? Email, string? Phone,
        List<string> DomainIds, string? Group, string? Source, string? Notes, bool Tentative,
        string? OwnerId, string? Cadence, List<string>? CustomerIds);
    private record SeedCbUser(string Id, string Name, string? Title, string? Department, string Email,
        string? Phone, string Role, List<string>? Skills);
    private record SeedRelation(string Id, string ColumbusId, string MsProfileId, short Score, string? Note);

    public async Task SeedAsync()
    {
        if (await db.MsProfiles.AnyAsync()) return;          // idempotent

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "seed.json");
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        await using var tx = await db.Database.BeginTransactionAsync();

        // Ids in the mockup are strings like "m14"; map them to the Guids used here.
        var boardId = new Dictionary<string, Guid>();
        var domainId = new Dictionary<string, Guid>();
        var userId = new Dictionary<string, Guid>();
        var profileId = new Dictionary<string, Guid>();
        var customerId = new Dictionary<string, Guid>();
        var groupId = new Dictionary<string, Guid>();
        var sourceId = new Dictionary<string, Guid>();
        var departmentId = new Dictionary<string, Guid>();

        Guid Taxonomy<T>(Dictionary<string, Guid> map, DbSet<T> set, string? name, Func<string, T> make) where T : class
        {
            name = string.IsNullOrWhiteSpace(name) ? "Unspecified" : name;
            if (map.TryGetValue(name, out var id)) return id;
            var entity = make(name);
            set.Add(entity);
            id = (Guid)typeof(T).GetProperty("Id")!.GetValue(entity)!;
            map[name] = id;
            return id;
        }

        foreach (var b in seed.Boards)
        {
            var board = new Dashboard
            {
                Slug = b.Id, Label = b.Label, Subtitle = b.Subtitle,
                Owner = b.Owner, Version = b.Version, UpdatedLabel = b.Updated, IsSystem = b.System
            };
            db.Dashboards.Add(board);
            boardId[b.Id] = board.Id;
        }

        foreach (var d in seed.Domains)
        {
            var domain = new Domain
            {
                DashboardId = d.Dept is not null && boardId.TryGetValue(d.Dept, out var bid) ? bid : null,
                Name = d.Name, Owner = d.Owner, Description = d.Desc, IsSystem = d.System
            };
            db.Domains.Add(domain);
            domainId[d.Id] = domain.Id;
        }

        foreach (var c in seed.Customers)
        {
            var customer = new Customer
            {
                Name = c.Name,
                Type = Enum.TryParse<CustomerType>(c.Type, true, out var t) ? t : CustomerType.Unknown
            };
            db.Customers.Add(customer);
            customerId[c.Id] = customer.Id;
        }

        foreach (var u in seed.ColumbusProfiles)
        {
            var user = new ColumbusUser
            {
                Name = u.Name, Title = u.Title,
                // Twelve of the 26 carry "" rather than a real address. ColumbusUser.Email
                // is unique-when-present, so a second "" would collide with the first;
                // mapping the blank to null is what makes that index hold.
                Email = string.IsNullOrWhiteSpace(u.Email) ? null : u.Email,
                Phone = u.Phone,
                Skills = u.Skills?.ToArray() ?? [],
                DepartmentId = Taxonomy(departmentId, db.CbDepartments, u.Department,
                    n => new CbDepartment { Name = n }),
                Role = u.Role switch
                {
                    "superadmin" => UserRole.SuperAdmin,
                    "admin" => UserRole.Admin,
                    _ => UserRole.Editor
                }
            };
            db.ColumbusUsers.Add(user);
            userId[u.Id] = user.Id;
        }

        foreach (var p in seed.MsProfiles)
        {
            var profile = MsProfile.Create(p.Name, string.IsNullOrWhiteSpace(p.Email) ? null : p.Email, "Microsoft Denmark");
            profile.Title = p.Title;
            profile.Phone = p.Phone;
            profile.Notes = p.Notes;
            profile.IsTentative = p.Tentative;
            profile.GroupId = Taxonomy(groupId, db.MsGroups, p.Group, n => new MsGroup { Name = n });
            profile.SourceId = Taxonomy(sourceId, db.MsSources, p.Source, n => new MsSource { Name = n });
            profile.Cadence = Enum.TryParse<ReminderCadence>(p.Cadence, true, out var c) ? c : ReminderCadence.None;
            db.MsProfiles.Add(profile);
            profileId[p.Id] = profile.Id;

            foreach (var d in p.DomainIds.Where(domainId.ContainsKey))
                db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = profile.Id, DomainId = domainId[d] });
            foreach (var cu in (p.CustomerIds ?? []).Where(customerId.ContainsKey))
                db.MsProfileCustomers.Add(new MsProfileCustomer { MsProfileId = profile.Id, CustomerId = customerId[cu] });
        }

        foreach (var r in seed.Relations.Where(r => userId.ContainsKey(r.ColumbusId) && profileId.ContainsKey(r.MsProfileId)))
            db.Relations.Add(new Relation
            {
                ColumbusUserId = userId[r.ColumbusId], MsProfileId = profileId[r.MsProfileId],
                Score = r.Score, Note = r.Note
            });

        foreach (var c in seed.Contacts.Where(c => profileId.ContainsKey(c.MsProfileId) && userId.ContainsKey(c.ById)))
            db.ContactEntries.Add(new ContactEntry
            {
                MsProfileId = profileId[c.MsProfileId], RegisteredByUserId = userId[c.ById],
                ContactedOn = DateOnly.Parse(c.Date)
            });

        await db.SaveChangesAsync();

        /* Owners are set last, because the rule is that an owner must already
           hold a relation — and the relations only exist once the line above
           has run (FR-43). */
        foreach (var p in seed.MsProfiles.Where(p => !string.IsNullOrEmpty(p.OwnerId)))
        {
            if (!userId.TryGetValue(p.OwnerId!, out var owner)) continue;
            var target = profileId[p.Id];
            if (!await db.Relations.AnyAsync(r => r.MsProfileId == target && r.ColumbusUserId == owner)) continue;
            (await db.MsProfiles.FindAsync(target))!.OwnerId = owner;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
