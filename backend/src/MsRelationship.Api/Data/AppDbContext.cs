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
    public DbSet<ColumbusUser> ColumbusUsers => Set<ColumbusUser>();
    public DbSet<MsProfile> MsProfiles => Set<MsProfile>();
    public DbSet<MsProfileDomain> MsProfileDomains => Set<MsProfileDomain>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MsProfileCustomer> MsProfileCustomers => Set<MsProfileCustomer>();
    public DbSet<Relation> Relations => Set<Relation>();

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

        b.Entity<ColumbusUser>().HasIndex(x => x.Email).IsUnique();
        /* Stored as text, not the default int, so the database stays readable and a
           later reordering of the enum members can't silently remap existing rows. */
        b.Entity<ColumbusUser>().Property(x => x.Role).HasConversion<string>();
        b.Entity<ColumbusUser>().Property(x => x.Status).HasConversion<string>();

        b.Entity<MsProfile>().HasIndex(x => x.IdentityKey).IsUnique();
        b.Entity<MsProfile>().Property(x => x.Cadence).HasConversion<string>();

        b.Entity<MsProfileDomain>().HasKey(x => new { x.MsProfileId, x.DomainId });
        b.Entity<MsProfileCustomer>().HasKey(x => new { x.MsProfileId, x.CustomerId });
        b.Entity<Customer>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Customer>().Property(x => x.Type).HasConversion<string>();

        b.Entity<Relation>(e =>
        {
            e.HasIndex(x => new { x.ColumbusUserId, x.MsProfileId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_relations_score_range", "score BETWEEN -3 AND 3"));
        });
    }
}
