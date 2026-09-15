using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MsGroup> MsGroups => Set<MsGroup>();
    public DbSet<MsSource> MsSources => Set<MsSource>();
    public DbSet<CbDepartment> CbDepartments => Set<CbDepartment>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<Domain> Domains => Set<Domain>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MsGroup>().HasIndex(x => x.Name).IsUnique();
        b.Entity<MsSource>().HasIndex(x => x.Name).IsUnique();
        b.Entity<CbDepartment>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Dashboard>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Domain>()
            .HasOne(d => d.Dashboard).WithMany()
            .HasForeignKey(d => d.DashboardId).OnDelete(DeleteBehavior.Restrict);
        /* A domain name is unique per dashboard, not globally: "Business Applications"
           may exist on two boards. */
        b.Entity<Domain>().HasIndex(x => new { x.DashboardId, x.Name }).IsUnique();
    }
}
