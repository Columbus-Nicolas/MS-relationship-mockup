using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MsGroup> MsGroups => Set<MsGroup>();
    public DbSet<MsSource> MsSources => Set<MsSource>();
    public DbSet<CbDepartment> CbDepartments => Set<CbDepartment>();
    public DbSet<MsDomain> MsDomains => Set<MsDomain>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MsGroup>().ToTable("ms_groups").HasIndex(x => x.Name).IsUnique();
        b.Entity<MsSource>().ToTable("ms_sources").HasIndex(x => x.Name).IsUnique();
        b.Entity<CbDepartment>().ToTable("cb_departments").HasIndex(x => x.Name).IsUnique();
        b.Entity<MsDomain>().ToTable("ms_domains").HasIndex(x => x.Name).IsUnique();
    }
}
