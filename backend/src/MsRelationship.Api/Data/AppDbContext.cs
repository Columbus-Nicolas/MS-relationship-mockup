using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MsGroup> MsGroups => Set<MsGroup>();
    public DbSet<MsSource> MsSources => Set<MsSource>();
    public DbSet<CbDepartment> CbDepartments => Set<CbDepartment>();
    public DbSet<MsDomain> MsDomains => Set<MsDomain>();
    public DbSet<ColumbusUser> ColumbusUsers => Set<ColumbusUser>();
    public DbSet<MsProfile> MsProfiles => Set<MsProfile>();
    public DbSet<MsProfileDomain> MsProfileDomains => Set<MsProfileDomain>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MsGroup>().ToTable("ms_groups").HasIndex(x => x.Name).IsUnique();
        b.Entity<MsSource>().ToTable("ms_sources").HasIndex(x => x.Name).IsUnique();
        b.Entity<CbDepartment>().ToTable("cb_departments").HasIndex(x => x.Name).IsUnique();
        b.Entity<MsDomain>().ToTable("ms_domains").HasIndex(x => x.Name).IsUnique();

        b.Entity<ColumbusUser>(e =>
        {
            e.ToTable("columbus_users");
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.EntraObjectId).IsUnique().HasFilter("entra_object_id IS NOT NULL");
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<MsProfile>(e =>
        {
            e.ToTable("ms_profiles");
            e.Property(x => x.IdentityKey)
             .HasComputedColumnSql(
                 "CASE WHEN email IS NULL OR btrim(email) = '' " +
                 "THEN lower(btrim(name)) || '|' || lower(btrim(coalesce(organization, ''))) " +
                 "ELSE lower(btrim(email)) END", stored: true);
            e.HasIndex(x => x.IdentityKey).IsUnique().HasFilter("merged_into_id IS NULL");
        });

        b.Entity<MsProfileDomain>(e =>
        {
            e.ToTable("ms_profile_domains");
            e.HasKey(x => new { x.MsProfileId, x.DomainId });
        });
    }
}
