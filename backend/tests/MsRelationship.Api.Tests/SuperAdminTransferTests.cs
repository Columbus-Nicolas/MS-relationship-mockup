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
    // to oneself is therefore a deliberate no-op rather than an error.
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

    [Fact]
    public async Task Exactly_one_super_admin_survives_a_failed_transfer()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var archivedTarget = await Seed.UserAsync(db);
        archivedTarget.Status = UserStatus.Archived;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(holder.Id, archivedTarget.Id));

        var superAdmins = await db.ColumbusUsers.CountAsync(u => u.Role == UserRole.SuperAdmin);
        Assert.Equal(1, superAdmins);
        Assert.Equal(UserRole.SuperAdmin, (await db.ColumbusUsers.SingleAsync(u => u.Id == holder.Id)).Role);
    }
}
