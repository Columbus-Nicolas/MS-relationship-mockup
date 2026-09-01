using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// The developer account is added <em>on top of</em> the approved mockup dataset, never in place
/// of any of it. These tests pin both halves of that: the account exists and is usable, and the
/// synthetic data it sits alongside is left byte-for-byte alone.
/// </summary>
[Collection("postgres")]
public class DevSeederTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeds_an_active_developer_account_with_the_configured_role()
    {
        await using var db = fixture.NewContext();
        await DevSeeder.SeedDevUserAsync(db);

        var dev = await db.ColumbusUsers.SingleAsync(u => u.Email == DevSeeder.DevUserEmail);
        Assert.Equal(DevSeeder.DevUserName, dev.Name);
        Assert.Equal(DevSeeder.DevUserRole, dev.Role);
        Assert.Equal(UserStatus.Active, dev.Status);

        // Null rather than a fabricated department, so the account cannot show up as a phantom
        // entry in the Columbus Profiles department filter.
        Assert.Null(dev.DepartmentId);
    }

    /// <summary>
    /// Dev mode migrates and seeds on every boot, so a developer restarting the API a dozen times
    /// must not accumulate a dozen accounts — or trip the unique index on email trying.
    /// </summary>
    [Fact]
    public async Task Seeding_twice_is_a_no_op()
    {
        await using var db = fixture.NewContext();
        await DevSeeder.SeedDevUserAsync(db);
        await DevSeeder.SeedDevUserAsync(db);

        Assert.Equal(1, await db.ColumbusUsers.CountAsync(u => u.Email == DevSeeder.DevUserEmail));
    }

    [Fact]
    public async Task Leaves_the_approved_dataset_untouched()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        var profiles = await db.MsProfiles.CountAsync();
        var domains = await db.MsDomains.CountAsync();
        var relations = await db.Relations.CountAsync();
        var users = await db.ColumbusUsers.CountAsync();

        await DevSeeder.SeedDevUserAsync(db);

        Assert.Equal(profiles, await db.MsProfiles.CountAsync());
        Assert.Equal(domains, await db.MsDomains.CountAsync());
        Assert.Equal(relations, await db.Relations.CountAsync());
        Assert.Equal(users + 1, await db.ColumbusUsers.CountAsync());   // the one addition, and only one
    }

    /// <summary>
    /// Documents the deviation rather than asserting it is fine: seed.json already assigns
    /// SuperAdmin to Jesper Winther, so with <see cref="DevSeeder.DevUserRole"/> set to SuperAdmin
    /// a dev database holds two — against the "exactly one Super Admin" invariant that
    /// <c>SuperAdminTransfer</c> documents. Nothing in the application queries for a single
    /// holder, so this is contained to local databases. If <see cref="DevSeeder.DevUserRole"/> is
    /// ever changed to Admin to restore the invariant, this test states the new expectation
    /// instead of failing mysteriously.
    /// </summary>
    [Fact]
    public async Task Records_how_many_super_admins_a_dev_database_ends_up_with()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);
        await DevSeeder.SeedDevUserAsync(db);

        var expected = DevSeeder.DevUserRole == UserRole.SuperAdmin ? 2 : 1;
        Assert.Equal(expected, await db.ColumbusUsers.CountAsync(u => u.Role == UserRole.SuperAdmin));
    }
}
