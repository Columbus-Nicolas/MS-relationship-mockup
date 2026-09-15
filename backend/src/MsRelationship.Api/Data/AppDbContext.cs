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
    public DbSet<RelationHistory> RelationHistory => Set<RelationHistory>();
    public DbSet<ContactEntry> ContactEntries => Set<ContactEntry>();

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

        /* Stored as text like every other enum here, and doubly so for this table:
           relation_history exists to answer "what changed" by reading the database
           directly, and a bare 0/1/2 defeats that. It also means a later reordering
           of the enum members can't silently reinterpret rows nothing can correct. */
        b.Entity<RelationHistory>().Property(x => x.ChangeType).HasConversion<string>();
    }

    /* DbContext exposes four public virtual save entry points, not two: SaveChanges()
       and SaveChangesAsync(ct) are only forwarders to SaveChanges(bool) and
       SaveChangesAsync(bool, ct) — that is how the base class itself implements them.
       Guarding just the parameterless pair would still catch calls that go through
       them, but SaveChanges(bool)/SaveChangesAsync(bool, ct) would remain a side door
       straight to the database for any caller that invokes them directly. So the
       guard lives on the two bool-taking overloads, the ones every save eventually
       reaches, and the parameterless pair forwards to those instead of to base. */
    public override int SaveChanges() =>
        SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, ct);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        GuardHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// The history table is the record of what happened. Editing it would make it a
    /// record of what someone wanted to have happened, so this refuses any tracked
    /// RelationHistory entry that reaches SaveChanges/SaveChangesAsync as Modified or
    /// Deleted. It cannot see ExecuteUpdate, ExecuteDelete, or raw SQL — those bypass
    /// the change tracker entirely, so no SaveChanges override can catch them.
    private void GuardHistory()
    {
        foreach (var entry in ChangeTracker.Entries<RelationHistory>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    "relation_history is append-only: rows may be added, never changed or removed.");
    }
}
