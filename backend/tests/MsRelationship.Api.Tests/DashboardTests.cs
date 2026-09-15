using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Dashboards;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class DashboardTests
{
    private readonly PostgresFixture _pg;
    public DashboardTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_dashboard_can_be_created_at_runtime()
    {
        await using var db = _pg.NewContext();
        db.Dashboards.Add(new Dashboard { Slug = "cloud", Label = "Cloud" });
        await db.SaveChangesAsync();
        Assert.True(await db.Dashboards.AnyAsync(d => d.Slug == "cloud"));
    }

    [Fact]
    public async Task A_dashboard_holding_domains_cannot_be_deleted()
    {
        await using var db = _pg.NewContext();
        var board = new Dashboard { Slug = "modern-work", Label = "Modern Work" };
        db.Dashboards.Add(board);
        db.Domains.Add(new Domain { DashboardId = board.Id, Name = "Teams & Adoption" });
        await db.SaveChangesAsync();

        var result = await new DashboardService(db).DeleteAsync(board.Id);

        Assert.False(result.Deleted);
        Assert.True(result.HasDomains);
        Assert.True(await db.Dashboards.AnyAsync(d => d.Id == board.Id));
    }

    [Fact]
    public async Task An_empty_dashboard_can_be_deleted()
    {
        await using var db = _pg.NewContext();
        var board = new Dashboard { Slug = "temporary", Label = "Temporary" };
        db.Dashboards.Add(board);
        await db.SaveChangesAsync();

        var result = await new DashboardService(db).DeleteAsync(board.Id);

        Assert.True(result.Deleted);
        Assert.False(await db.Dashboards.AnyAsync(d => d.Id == board.Id));
    }

    [Fact]
    public async Task The_landing_dashboard_cannot_be_deleted()
    {
        await using var db = _pg.NewContext();
        var board = new Dashboard { Slug = "landing", Label = "Landing", IsSystem = true };
        db.Dashboards.Add(board);
        await db.SaveChangesAsync();

        var result = await new DashboardService(db).DeleteAsync(board.Id);

        Assert.False(result.Deleted);
        Assert.True(result.IsSystem);
    }

    [Fact]
    public async Task A_nonexistent_dashboard_cannot_be_deleted()
    {
        await using var db = _pg.NewContext();

        var result = await new DashboardService(db).DeleteAsync(Guid.NewGuid());

        Assert.False(result.Deleted);
        Assert.False(result.HasDomains);
        Assert.False(result.IsSystem);
    }
}
