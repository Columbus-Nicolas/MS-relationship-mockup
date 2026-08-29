using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.ColumbusUsers;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ArchiveTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Archiving_keeps_the_relations()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, 3, "trusted", user.Id);
        var admin = await Seed.UserAsync(db);

        await new UserArchiver(db).ArchiveAsync(user.Id, admin.Id);

        var stored = await db.ColumbusUsers.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Archived, stored.Status);
        Assert.NotNull(stored.ArchivedAt);
        Assert.True(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
    }

    [Fact]
    public async Task Archiving_writes_history_for_each_relation()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var first = await Seed.ProfileAsync(db);
        var second = await Seed.ProfileAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, first.Id, 1, "", user.Id);
        await writer.UpsertAsync(user.Id, second.Id, 2, "", user.Id);
        var admin = await Seed.UserAsync(db);

        await new UserArchiver(db).ArchiveAsync(user.Id, admin.Id);

        var archived = await db.RelationHistory
            .CountAsync(h => h.ColumbusUserId == user.Id && h.ChangeType == RelationChangeType.UserArchived);
        Assert.Equal(2, archived);
    }

    [Fact]
    public async Task The_super_admin_cannot_be_archived()
    {
        await using var db = fixture.NewContext();
        var superAdmin = await Seed.UserAsync(db);
        superAdmin.Role = UserRole.SuperAdmin;
        await db.SaveChangesAsync();
        var admin = await Seed.UserAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UserArchiver(db).ArchiveAsync(superAdmin.Id, admin.Id));
    }

    [Fact]
    public async Task Archiving_twice_is_idempotent_and_does_not_duplicate_history()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var profile = await Seed.ProfileAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, 1, "", user.Id);
        var admin = await Seed.UserAsync(db);
        var archiver = new UserArchiver(db);

        await archiver.ArchiveAsync(user.Id, admin.Id);
        var firstArchivedAt = (await db.ColumbusUsers.SingleAsync(u => u.Id == user.Id)).ArchivedAt;

        await archiver.ArchiveAsync(user.Id, admin.Id);

        var stored = await db.ColumbusUsers.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Archived, stored.Status);
        Assert.Equal(firstArchivedAt, stored.ArchivedAt);
        var archivedHistoryCount = await db.RelationHistory
            .CountAsync(h => h.ColumbusUserId == user.Id && h.ChangeType == RelationChangeType.UserArchived);
        Assert.Equal(1, archivedHistoryCount);
    }
}
