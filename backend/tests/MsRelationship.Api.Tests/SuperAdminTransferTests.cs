using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.ColumbusUsers;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SuperAdminTransferTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transfer_demotes_the_holder_to_admin()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var target = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        await new SuperAdminTransfer(db).TransferAsync(holder.Id, target.Id);

        Assert.Equal(UserRole.Admin, (await db.ColumbusUsers.SingleAsync(u => u.Id == holder.Id)).Role);
        Assert.Equal(UserRole.SuperAdmin, (await db.ColumbusUsers.SingleAsync(u => u.Id == target.Id)).Role);
    }

    [Fact]
    public async Task Only_the_current_holder_can_transfer()
    {
        await using var db = fixture.NewContext();
        var impostor = await Seed.UserAsync(db);
        impostor.Role = UserRole.Admin;
        var target = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(impostor.Id, target.Id));
    }

    [Fact]
    public async Task An_archived_user_cannot_receive_the_role()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var target = await Seed.UserAsync(db);
        target.Status = UserStatus.Archived;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(holder.Id, target.Id));
    }

    // Given the "exactly one Super Admin" invariant, the only way `toUserId` can already hold
    // the role is if it is the same user as `fromUserId` (two distinct rows both holding
    // SuperAdmin would itself be an invariant violation this class never produces). Transferring
    // to oneself is therefore a deliberate no-op rather than an error — but only once the caller
    // is confirmed to actually hold the role (see the next test for the non-holder case).
    [Fact]
    public async Task Transferring_to_self_is_a_no_op()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        await db.SaveChangesAsync();

        await new SuperAdminTransfer(db).TransferAsync(holder.Id, holder.Id);

        Assert.Equal(UserRole.SuperAdmin, (await db.ColumbusUsers.SingleAsync(u => u.Id == holder.Id)).Role);
    }

    // The self-transfer no-op must never short-circuit the holder check. Without this ordering,
    // TransferAsync(bob.Id, bob.Id) for a non-holder bob would return a silent success (a 204 at
    // the endpoint) instead of rejecting an unauthorized caller.
    [Fact]
    public async Task Self_transfer_by_a_non_holder_still_throws()
    {
        await using var db = fixture.NewContext();
        var bob = await Seed.UserAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(bob.Id, bob.Id));
    }

    // Proves atomicity for real, not just that guards run before any mutation. The archived
    // target is rejected by the *second* write (the conditional promote), which only runs after
    // the conditional demote has already executed as a live UPDATE inside the still-open
    // transaction. If the transaction did not roll that demote back when the promote's predicate
    // failed to match, this assertion would see the holder wrongly left as Admin.
    [Fact]
    public async Task A_failed_promote_rolls_back_the_demote()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var archivedTarget = await Seed.UserAsync(db);
        archivedTarget.Status = UserStatus.Archived;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(holder.Id, archivedTarget.Id));

        await using var verifyDb = fixture.NewContext();
        var superAdmins = await verifyDb.ColumbusUsers.CountAsync(u => u.Role == UserRole.SuperAdmin);
        Assert.Equal(1, superAdmins);
        Assert.Equal(UserRole.SuperAdmin, (await verifyDb.ColumbusUsers.SingleAsync(u => u.Id == holder.Id)).Role);
    }

    /// <summary>
    /// Reproduces the same-holder double-transfer race deterministically, with no threads or
    /// sleeps — mirroring <c>RelationWriterTests.Concurrent_updates_to_the_same_relation_are_caught</c>.
    /// <see cref="ColumbusUser"/> carries no concurrency token, so the guard is the conditional
    /// UPDATE inside <see cref="SuperAdminTransfer.TransferAsync"/> itself: two contexts both
    /// load the holder while it is still Super Admin, one context fully transfers and commits,
    /// and the other's later conditional UPDATE — issued from its now-stale belief that the
    /// holder still qualifies — matches zero rows and throws instead of minting a second Super
    /// Admin.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_transfers_from_the_same_holder_do_not_both_succeed()
    {
        await using var seedDb = fixture.NewContext();
        var holder = await Seed.UserAsync(seedDb);
        holder.Role = UserRole.SuperAdmin;
        var targetA = await Seed.UserAsync(seedDb);
        var targetB = await Seed.UserAsync(seedDb);
        await seedDb.SaveChangesAsync();

        await using var dbA = fixture.NewContext();
        await using var dbB = fixture.NewContext();

        // Both contexts load (and thus track) the holder at Role == SuperAdmin before either
        // one commits a transfer — the same setup RelationWriterTests uses for Relation.
        await dbA.ColumbusUsers.SingleAsync(u => u.Id == holder.Id);
        await dbB.ColumbusUsers.SingleAsync(u => u.Id == holder.Id);

        // Context B completes and commits a full transfer first.
        await new SuperAdminTransfer(dbB).TransferAsync(holder.Id, targetB.Id);

        // Context A's transfer, still working from its stale "holder is Super Admin" snapshot,
        // must fail rather than succeed and mint a second Super Admin.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(dbA).TransferAsync(holder.Id, targetA.Id));

        await using var verifyDb = fixture.NewContext();
        var superAdmins = await verifyDb.ColumbusUsers.CountAsync(u => u.Role == UserRole.SuperAdmin);
        Assert.Equal(1, superAdmins);
        Assert.Equal(UserRole.SuperAdmin, (await verifyDb.ColumbusUsers.SingleAsync(u => u.Id == targetB.Id)).Role);
    }
}
