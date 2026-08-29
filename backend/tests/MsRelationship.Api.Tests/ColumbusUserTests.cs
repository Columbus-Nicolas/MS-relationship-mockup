using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ColumbusUserTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Users_default_to_standard_and_active()
    {
        await using var db = fixture.NewContext();
        var user = new ColumbusUser { Id = Guid.NewGuid(), Email = "new.person@columbusglobal.com", Name = "New Person" };
        db.ColumbusUsers.Add(user);
        await db.SaveChangesAsync();

        await using var read = fixture.NewContext();
        var stored = await read.ColumbusUsers.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserRole.Standard, stored.Role);
        Assert.Equal(UserStatus.Active, stored.Status);
    }

    [Fact]
    public async Task Email_is_unique()
    {
        var email = $"dupe-{Guid.NewGuid():N}@columbusglobal.com";
        await using var db = fixture.NewContext();
        db.ColumbusUsers.Add(new ColumbusUser { Id = Guid.NewGuid(), Email = email, Name = "First" });
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.ColumbusUsers.Add(new ColumbusUser { Id = Guid.NewGuid(), Email = email, Name = "Second" });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
