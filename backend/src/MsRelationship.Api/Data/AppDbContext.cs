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
        b.Entity<ColumbusUser>()
            .HasOne(u => u.Department).WithMany()
            .HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<MsProfile>(e =>
        {
            e.HasIndex(x => x.IdentityKey).IsUnique();
            e.Property(x => x.Cadence).HasConversion<string>();
            e.HasOne<MsGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MsSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ColumbusUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            /* Self-referencing: a tombstone points at the profile it was merged
               into, so the old id still resolves to a live person (Task 13). */
            e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MsProfileDomain>(e =>
        {
            e.HasKey(x => new { x.MsProfileId, x.DomainId });
            e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MsProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain>().WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MsProfileCustomer>(e =>
        {
            e.HasKey(x => new { x.MsProfileId, x.CustomerId });
            e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MsProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Customer>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Customer>().Property(x => x.Type).HasConversion<string>();

        b.Entity<Relation>(e =>
        {
            e.HasIndex(x => new { x.ColumbusUserId, x.MsProfileId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_relations_score_range", "score BETWEEN -3 AND 3"));
            e.HasOne<ColumbusUser>().WithMany().HasForeignKey(x => x.ColumbusUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MsProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ContactEntry>(e =>
        {
            e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MsProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ColumbusUser>().WithMany().HasForeignKey(x => x.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        /* Every relationship above is declared without a navigation property, using
           HasOne<T>().WithMany(). The constraint is what is wanted here — the model
           could not keep its own invariants with two foreign keys in it — but the
           object graph is not: the merge in Task 13 rewrites MsProfileId directly on
           relation, contact and link rows, and navigations would invite EF to fix up
           an in-memory graph underneath that.

           DeleteBehavior.Restrict throughout, mirroring FR-26: deleting something
           another row still points at is refused, never cascaded and never quietly
           nulled. EF's convention for an optional foreign key is ClientSetNull, which
           would do exactly the quiet nulling — so every one of these says Restrict
           out loud rather than relying on a default.

           relation_history is deliberately absent from this list and must stay that
           way. It has no foreign keys — not on MsProfileId, not on ColumbusUserId,
           not on ChangedByUserId — because history has to outlive what it describes:
           a row must still say what a score was and who set it after the profile,
           the user, or the relation it refers to is gone. Adding foreign keys here
           would make the record deletable by proxy, which is the one thing an
           append-only table must not be. */

        /* Stored as text like every other enum here, and doubly so for this table:
           relation_history exists to answer "what changed" by reading the database
           directly, and a bare 0/1/2 defeats that. It also means a later reordering
           of the enum members can't silently reinterpret rows nothing can correct. */
        b.Entity<RelationHistory>().Property(x => x.ChangeType).HasConversion<string>();

        /* The only lookup index the foreign keys above do not already provide.
           Every other hot column is either a foreign key (EF indexes those) or the
           leading column of an existing key — relations(columbus_user_id) leads the
           unique pair, and both link tables' ms_profile_id leads their primary key.
           relation_history has no foreign keys to index it, and reading one
           person's history newest-first is what Stage 2's undo will do. */
        b.Entity<RelationHistory>().HasIndex(x => new { x.MsProfileId, x.ChangedAt });
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
