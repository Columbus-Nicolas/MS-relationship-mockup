using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

/// <summary>
/// Adds the one account a developer signs in as locally, on top of — never instead of — the
/// mockup dataset <see cref="Seeder"/> loads. Nothing here touches Microsoft profiles, domains,
/// groups or relations: the synthetic data stays exactly as approved, and this only appends a
/// person to <c>columbus_users</c>.
///
/// A dedicated account rather than reusing one of the fourteen seeded people, because those rows
/// are demo content — they get archived, re-roled and edited during a walkthrough, and the
/// account you sign in with should not be one that a demo can lock you out of.
///
/// Idempotent on email, matching how <see cref="Seeder.SeedAsync"/> guards itself, so the
/// migrate-and-seed step can run on every boot without accumulating duplicates. Email is the
/// right key: <c>AppDbContext</c> puts a unique index on it, and it is what
/// <see cref="Auth.MembershipMiddleware"/> resolves the caller by when no Entra object id has
/// been bound yet — which is always the case here, since dev mode issues no <c>oid</c>.
///
/// <see cref="ColumbusUser.DepartmentId"/> is left null. The column is nullable, and picking a
/// department would mean either depending on a specific taxonomy row existing (coupling this to
/// seed.json's contents) or inventing one, which would show up as a phantom department in the
/// Columbus Profiles filters.
/// </summary>
public static class DevSeeder
{
    public const string DevUserEmail = "dev@columbusglobal.example";
    public const string DevUserName = "Developer";
    public const string DevUserTitle = "Local development account";

    /// <summary>
    /// The role the local account holds. SuperAdmin was chosen deliberately, and it is worth
    /// knowing what that costs: <c>SuperAdminTransfer</c> documents the invariant "there is
    /// exactly one Super Admin at all times" (R-01), and seed.json already assigns that role to
    /// Jesper Winther — so a dev database holds two.
    ///
    /// That is contained rather than harmless. No unique index constrains the role, no query in
    /// the application selects "the" Super Admin with <c>SingleAsync</c>, and the two tests that
    /// count Super Admins build their own data after a reset, so nothing breaks. What does change
    /// is confined to dev databases: neither Super Admin can be archived (<c>UserArchiver</c>
    /// refuses), and a transfer between the two resolves to one holder instead of erroring.
    ///
    /// Change this single constant to <see cref="UserRole.Admin"/> to restore R-01 exactly. Admin
    /// satisfies both named policies and reaches every page, so nothing about local access is
    /// lost by doing so.
    /// </summary>
    public const UserRole DevUserRole = UserRole.SuperAdmin;

    public static async Task SeedDevUserAsync(AppDbContext db)
    {
        if (await db.ColumbusUsers.AnyAsync(u => u.Email == DevUserEmail)) return;

        db.ColumbusUsers.Add(new ColumbusUser
        {
            Id = Guid.NewGuid(),
            Email = DevUserEmail,
            Name = DevUserName,
            Title = DevUserTitle,
            DepartmentId = null,
            Skills = [],
            Role = DevUserRole,
            Status = UserStatus.Active
        });

        await db.SaveChangesAsync();
    }
}
