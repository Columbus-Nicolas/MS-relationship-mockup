using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ColumbusUserTests
{
    private readonly PostgresFixture _pg;
    public ColumbusUserTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_user_defaults_to_editor_and_active()
    {
        await using var db = _pg.NewContext();
        var user = new ColumbusUser { Name = "Mette Kirkegaard", Email = "mette@columbusglobal.example" };
        db.ColumbusUsers.Add(user);
        await db.SaveChangesAsync();

        var saved = await db.ColumbusUsers.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserRole.Editor, saved.Role);
        Assert.Equal(UserStatus.Active, saved.Status);
    }

    [Fact]
    public async Task Two_users_cannot_share_an_email()
    {
        await using var db = _pg.NewContext();
        db.ColumbusUsers.Add(new ColumbusUser { Name = "A", Email = "same@columbusglobal.example" });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.ColumbusUsers.Add(new ColumbusUser { Name = "B", Email = "same@columbusglobal.example" });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_users_with_no_email_can_both_be_saved()
    {
        await using var db = _pg.NewContext();
        var first = new ColumbusUser { Name = "No Email One" };
        db.ColumbusUsers.Add(first);
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        var second = new ColumbusUser { Name = "No Email Two" };
        other.ColumbusUsers.Add(second);
        await other.SaveChangesAsync();

        Assert.True(await other.ColumbusUsers.AnyAsync(u => u.Id == first.Id && u.Email == null));
        Assert.True(await other.ColumbusUsers.AnyAsync(u => u.Id == second.Id && u.Email == null));
    }
}
