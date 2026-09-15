# Backend Foundation (Stage 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A running API over Postgres that holds the whole mockup data set, where every write is attributable to a person and a time, and duplicate Microsoft people cannot quietly accumulate.

**Architecture:** One ASP.NET Core 9 Web API project plus one test project. EF Core 9 against Postgres 16, schema in snake_case, migrations checked in. Every mutation to a relationship goes through a single writer that appends to an immutable history table — the history cannot be bypassed because nothing else has write access to the relation tables. Authentication is a development stub in this stage (a header naming the acting user); Entra replaces it later without the call sites changing, because they only ever see `ICurrentUser`.

**Tech Stack:** .NET 9 (SDK pinned 9.0.306), ASP.NET Core, EF Core 9 (`Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`), Postgres 16 in docker compose, xUnit + `Testcontainers.PostgreSql`.

**Spec:** `docs/requirements-and-plan-v2.md` — Stage 1 in §4.3, requirements in §3. The mockup at tag `mockup-v1` (`index.html`) is the reference for every shape and every piece of wording.

## Global Constraints

- **.NET 9**, SDK pinned in `backend/global.json` to `9.0.306` with `rollForward: latestFeature`. `dotnet-ef` installed as a **local** tool, not global.
- **EF Core 9**, Postgres 16. Database naming is **snake_case** via `EFCore.NamingConventions`.
- **No country column anywhere.** An instance serves exactly one country (TEC-04a); country is configuration, not data.
- **Taxonomies are rows, not enums** (TEC-07): Microsoft groups, sources and Columbus departments are tables, so adding one needs no migration and no deployment.
- **Relationship score is `smallint` with `CHECK (score BETWEEN -3 AND 3)`** (FR-01). There is no second scale.
- **One relation per (Columbus person, Microsoft person)** pair, enforced by a unique index (FR-02).
- **History is append-only** (FR-32): acting user, timestamp, value before and after. No update and no delete on the history table.
- **An owner must already hold a relation to the person they own** (FR-43). Somebody nobody knows has no owner — that is a finding, not an error.
- **Empty means Unknown, never Never.** An empty contact log means nobody wrote it down (FR-45). The API returns null; the wording belongs to the frontend.
- **No code is copied from the `first_iteration` branch.** Only the technology choice carries over (§6).
- **`.env` is in `.gitignore` from the first commit.** Only `.env.example` is committed, and it holds no real values.
- Tests run against a **real Postgres in a container**, never a mock or an in-memory provider — identity, merge and history are exactly the things an in-memory provider gets wrong.

---

## File Structure

```
backend/
  global.json                       SDK pin
  .config/dotnet-tools.json         dotnet-ef as a local tool
  MsRelationship.sln
  src/MsRelationship.Api/
    Program.cs                      composition root, health endpoint
    Auth/ICurrentUser.cs            the acting user, however they were identified
    Auth/DevUserMiddleware.cs       Stage 1 stub: X-Dev-User header -> ICurrentUser
    Data/AppDbContext.cs            DbSets, constraints, snake_case
    Data/Entities/*.cs              one file per entity
    Data/Seed/MockupSeeder.cs       loads seed.json
    Data/Seed/seed.json             the mockup data set
    Features/Taxonomies/            groups, sources, departments
    Features/Dashboards/            dashboards + domains
    Features/ColumbusUsers/
    Features/MsProfiles/            identity key, match, merge
    Features/Customers/
    Features/Relations/RelationWriter.cs   the only way into relations + history
    Features/Contacts/              the contact log
    Migrations/
  tests/MsRelationship.Api.Tests/
    PostgresFixture.cs              Testcontainers, one container per test run
    *Tests.cs                       one file per feature
docker-compose.yml                  Postgres 16 (+ Mailpit, unused until Stage 6)
.env.example
```

Files are split by feature rather than by layer: the entity, the rules about it and its endpoints sit together, because they change together.

---

### Task 1: Walking skeleton — solution, container, health endpoint

**Files:**
- Create: `backend/global.json`, `backend/.config/dotnet-tools.json`, `backend/MsRelationship.sln`
- Create: `backend/src/MsRelationship.Api/MsRelationship.Api.csproj`, `backend/src/MsRelationship.Api/Program.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MsRelationship.Api.Tests.csproj`, `backend/tests/MsRelationship.Api.Tests/HealthTests.cs`
- Create: `docker-compose.yml`, `.env.example`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: a runnable API and a test project that boots it via `WebApplicationFactory<Program>`. Later tasks add to both.

- [ ] **Step 1: Put `.env` out of reach before anything else exists**

Append to `.gitignore`:

```gitignore
# Local environment — never committed. Copy .env.example to .env and fill it in.
.env
.DS_Store
bin/
obj/
```

Commit this on its own, first. The previous attempt committed `.env` and removed it four commits later; the fix is to make it impossible from commit one.

- [ ] **Step 2: Scaffold the solution and pin the SDK**

```bash
mkdir -p backend/src backend/tests
cd backend
cat > global.json <<'EOF'
{ "sdk": { "version": "9.0.306", "rollForward": "latestFeature" } }
EOF
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 9.0.*
dotnet new sln -n MsRelationship
dotnet new webapi -n MsRelationship.Api -o src/MsRelationship.Api --use-controllers
dotnet new xunit -n MsRelationship.Api.Tests -o tests/MsRelationship.Api.Tests
dotnet sln add src/MsRelationship.Api tests/MsRelationship.Api.Tests
dotnet add tests/MsRelationship.Api.Tests reference src/MsRelationship.Api
dotnet add src/MsRelationship.Api package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.4
dotnet add src/MsRelationship.Api package Microsoft.EntityFrameworkCore.Design --version 9.0.*
dotnet add src/MsRelationship.Api package EFCore.NamingConventions --version 9.0.0
dotnet add tests/MsRelationship.Api.Tests package Testcontainers.PostgreSql --version 4.14.0
dotnet add tests/MsRelationship.Api.Tests package Microsoft.AspNetCore.Mvc.Testing --version 9.0.*
```

- [ ] **Step 3: Write the failing test**

`backend/tests/MsRelationship.Api.Tests/HealthTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace MsRelationship.Api.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Run it and watch it fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter Health_returns_ok`
Expected: FAIL — `Program` is not accessible, or `/health` returns 404.

- [ ] **Step 5: Write the minimal Program.cs**

`backend/src/MsRelationship.Api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.MapGet("/health", () => Results.Text("ok"));
app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real composition root in tests.</summary>
public partial class Program { }
```

- [ ] **Step 6: Run it and watch it pass**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter Health_returns_ok`
Expected: PASS.

- [ ] **Step 7: Add the local infrastructure**

`docker-compose.yml`:

```yaml
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    ports: ["${POSTGRES_PORT}:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 5s
      retries: 10
volumes:
  pgdata:
```

`.env.example` — note the passwords are placeholders, and that the values from the previous attempt must not be reused:

```dotenv
# Copy to .env and choose new values. Never commit .env.
POSTGRES_DB=rmap
POSTGRES_USER=rmap
POSTGRES_PASSWORD=choose-a-new-one
POSTGRES_PORT=5433
ConnectionStrings__Default=Host=localhost;Port=5433;Database=rmap;Username=rmap;Password=choose-a-new-one
```

- [ ] **Step 8: Commit**

```bash
git add .gitignore backend docker-compose.yml .env.example
git commit -m "feat: walking skeleton — solution, health endpoint, local Postgres"
```

---

### Task 2: The database context and the first migration

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/PostgresFixture.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MigrationTests.cs`
- Modify: `backend/src/MsRelationship.Api/Program.cs`

**Interfaces:**
- Consumes: Task 1's project layout.
- Produces: `AppDbContext` (empty for now) registered in DI; `PostgresFixture` exposing `string ConnectionString` and `AppDbContext NewContext()`, which every later test class uses.

- [ ] **Step 1: Write the failing test**

`backend/tests/MsRelationship.Api.Tests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Testcontainers.PostgreSql;

namespace MsRelationship.Api.Tests;

/// One container for the whole test run. Tests that touch identity, merge or
/// history are exactly the ones an in-memory provider gets wrong, so they run
/// against the real thing.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

`backend/tests/MsRelationship.Api.Tests/MigrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MigrationTests
{
    private readonly PostgresFixture _pg;
    public MigrationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        await using var db = _pg.NewContext();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter Migrations_apply`
Expected: FAIL — `AppDbContext` does not exist.

- [ ] **Step 3: Write the context**

`backend/src/MsRelationship.Api/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MsRelationship.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
```

Register it in `Program.cs`, before `builder.Build()`:

```csharp
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());
```

- [ ] **Step 4: Create the first migration**

```bash
cd backend
dotnet ef migrations add Initial --project src/MsRelationship.Api
```

An empty model still produces a migration and the `__EFMigrationsHistory` table, which is what the test asserts.

- [ ] **Step 5: Run the test and watch it pass**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter Migrations_apply`
Expected: PASS. Docker must be running.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: EF Core context, snake_case naming, Postgres test fixture"
```

---

### Task 3: Taxonomies as rows

Microsoft groups, Microsoft sources and Columbus departments. These are the tables that make TEC-07 true: adding "Fabric Specialist" as a group must be an insert, not a migration.

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsGroup.cs`, `MsSource.cs`, `CbDepartment.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/TaxonomyTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `PostgresFixture`.
- Produces: `MsGroup`, `MsSource`, `CbDepartment`, each `{ Guid Id; string Name; int SortOrder; }` with a unique index on `Name`. Referenced by `MsProfile` (Task 6) and `ColumbusUser` (Task 5).

- [ ] **Step 1: Write the failing test**

`backend/tests/MsRelationship.Api.Tests/TaxonomyTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class TaxonomyTests
{
    private readonly PostgresFixture _pg;
    public TaxonomyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_group_can_be_added_without_a_migration()
    {
        await using var db = _pg.NewContext();
        db.MsGroups.Add(new MsGroup { Name = "Fabric Specialist", SortOrder = 99 });
        await db.SaveChangesAsync();

        Assert.True(await db.MsGroups.AnyAsync(g => g.Name == "Fabric Specialist"));
    }

    [Fact]
    public async Task Group_names_are_unique()
    {
        await using var db = _pg.NewContext();
        db.MsGroups.Add(new MsGroup { Name = "Leadership", SortOrder = 1 });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.MsGroups.Add(new MsGroup { Name = "Leadership", SortOrder = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run and watch both fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter TaxonomyTests`
Expected: FAIL — `MsGroup` does not exist.

- [ ] **Step 3: Write the entities**

`backend/src/MsRelationship.Api/Data/Entities/MsGroup.cs` (and the same shape in `MsSource.cs` and `CbDepartment.cs`):

```csharp
namespace MsRelationship.Api.Data.Entities;

/// The kind of role a Microsoft person holds. A row, not an enum: adding one
/// must never need a migration (TEC-07).
public class MsGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
```

In `AppDbContext`:

```csharp
public DbSet<MsGroup> MsGroups => Set<MsGroup>();
public DbSet<MsSource> MsSources => Set<MsSource>();
public DbSet<CbDepartment> CbDepartments => Set<CbDepartment>();

protected override void OnModelCreating(ModelBuilder b)
{
    b.Entity<MsGroup>().HasIndex(x => x.Name).IsUnique();
    b.Entity<MsSource>().HasIndex(x => x.Name).IsUnique();
    b.Entity<CbDepartment>().HasIndex(x => x.Name).IsUnique();
}
```

- [ ] **Step 4: Migrate and run**

```bash
cd backend && dotnet ef migrations add Taxonomies --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter TaxonomyTests
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: taxonomies as rows — groups, sources, departments"
```

---

### Task 4: Dashboards and domains

A dashboard is a department's view. A domain is a panel on it. Both are data, created at runtime (FR-21, FR-22), and a dashboard can only be deleted once it is empty (FR-26).

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/Dashboard.cs`, `Domain.cs`
- Create: `backend/src/MsRelationship.Api/Features/Dashboards/DashboardService.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`, `Program.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/DashboardTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`.
- Produces:
  - `Dashboard { Guid Id; string Slug; string Label; string? Subtitle; string? Owner; string? Version; string? UpdatedLabel; bool IsSystem; }`
  - `Domain { Guid Id; Guid? DashboardId; string Name; string? Owner; string? Description; bool IsSystem; int SortOrder; }`
  - `DashboardService.DeleteAsync(Guid id)` returning `DeleteResult { Deleted, HasDomains, IsSystem }`.

- [ ] **Step 1: Write the failing tests**

`backend/tests/MsRelationship.Api.Tests/DashboardTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter DashboardTests`
Expected: FAIL — `Dashboard` does not exist.

- [ ] **Step 3: Write the entities**

`backend/src/MsRelationship.Api/Data/Entities/Dashboard.cs`:

```csharp
namespace MsRelationship.Api.Data.Entities;

/// One department's view of Microsoft. Data, not code: adding a department is a
/// row, and its two pages follow from it (FR-21, FR-22).
public class Dashboard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// Route-safe and stable. Renaming the label never changes this, so links keep working.
    public required string Slug { get; set; }
    public required string Label { get; set; }
    public string? Subtitle { get; set; }
    public string? Owner { get; set; }
    public string? Version { get; set; }
    public string? UpdatedLabel { get; set; }
    /// The landing dashboard. Cannot be deleted (FR-26).
    public bool IsSystem { get; set; }
}
```

`backend/src/MsRelationship.Api/Data/Entities/Domain.cs`:

```csharp
namespace MsRelationship.Api.Data.Entities;

/// A category of Microsoft people, and a panel on its dashboard. Stored apart
/// from the people so Microsoft can reorganise without touching relationships (FR-05).
public class Domain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// Null for the system "Unmarked" domain, which belongs to no dashboard.
    public Guid? DashboardId { get; set; }
    public Dashboard? Dashboard { get; set; }
    public required string Name { get; set; }
    public string? Owner { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
}
```

In `AppDbContext`:

```csharp
public DbSet<Dashboard> Dashboards => Set<Dashboard>();
public DbSet<Domain> Domains => Set<Domain>();
```

and in `OnModelCreating`:

```csharp
b.Entity<Dashboard>().HasIndex(x => x.Slug).IsUnique();
b.Entity<Domain>()
    .HasOne(d => d.Dashboard).WithMany()
    .HasForeignKey(d => d.DashboardId).OnDelete(DeleteBehavior.Restrict);
/* A domain name is unique per dashboard, not globally: "Business Applications"
   may exist on two boards. */
b.Entity<Domain>().HasIndex(x => new { x.DashboardId, x.Name }).IsUnique();
```

- [ ] **Step 4: Write the service**

`backend/src/MsRelationship.Api/Features/Dashboards/DashboardService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.Dashboards;

public record DeleteResult(bool Deleted, bool HasDomains, bool IsSystem);

public class DashboardService(AppDbContext db)
{
    /// Deleting a dashboard must not be able to orphan a domain, a profile or a
    /// relationship, so an occupied board is refused rather than cascaded (FR-26).
    public async Task<DeleteResult> DeleteAsync(Guid id)
    {
        var board = await db.Dashboards.FindAsync(id);
        if (board is null) return new DeleteResult(false, false, false);
        if (board.IsSystem) return new DeleteResult(false, false, true);

        if (await db.Domains.AnyAsync(d => d.DashboardId == id))
            return new DeleteResult(false, true, false);

        db.Dashboards.Remove(board);
        await db.SaveChangesAsync();
        return new DeleteResult(true, false, false);
    }
}
```

Register it in `Program.cs`: `builder.Services.AddScoped<DashboardService>();`

- [ ] **Step 5: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add Dashboards --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter DashboardTests
git add backend && git commit -m "feat: dashboards and domains, deletable only when empty"
```
Expected: 4 passing tests.

---

### Task 5: Columbus users

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/ColumbusUser.cs`, `UserRole.cs`, `UserStatus.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/ColumbusUserTests.cs`

**Interfaces:**
- Consumes: `CbDepartment` from Task 3.
- Produces: `ColumbusUser { Guid Id; string Name; string? Title; Guid? DepartmentId; string Email; string? Phone; UserRole Role; UserStatus Status; string[] Skills; }`. `Email` is unique. Referenced by relations (Task 9), owner (Task 12) and the contact log (Task 12).

- [ ] **Step 1: Write the failing test**

```csharp
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
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter ColumbusUserTests`
Expected: FAIL — `ColumbusUser` does not exist.

- [ ] **Step 3: Write the entity**

```csharp
namespace MsRelationship.Api.Data.Entities;

/// Roles collapsed to two plus an owner when editing became open (§3.11):
/// everybody can edit, so a role only says who may administer.
public enum UserRole { Editor, Admin, SuperAdmin }
public enum UserStatus { Active, Archived }

public class ColumbusUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Title { get; set; }
    public Guid? DepartmentId { get; set; }
    public CbDepartment? Department { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.Editor;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string[] Skills { get; set; } = [];
}
```

In `AppDbContext`:

```csharp
public DbSet<ColumbusUser> ColumbusUsers => Set<ColumbusUser>();
```

and in `OnModelCreating`:

```csharp
b.Entity<ColumbusUser>().HasIndex(x => x.Email).IsUnique();
b.Entity<ColumbusUser>().Property(x => x.Role).HasConversion<string>();
b.Entity<ColumbusUser>().Property(x => x.Status).HasConversion<string>();
```

Storing the enums as text keeps the database readable and survives reordering the enum.

- [ ] **Step 4: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add ColumbusUsers --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter ColumbusUserTests
git add backend && git commit -m "feat: Columbus users with role and status"
```

---

### Task 6: Microsoft profiles and the identity key

The rule that stops "Anne Berg" existing three times after two import rounds (FR-18).

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsProfile.cs`, `ReminderCadence.cs`
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/IdentityKey.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/IdentityKeyTests.cs`

**Interfaces:**
- Consumes: `MsGroup`, `MsSource`.
- Produces:
  - `IdentityKey.For(string? email, string name, string? organization)` returning `string`.
  - `MsProfile { Guid Id; string Name; string? Title; string? Email; string? Phone; string? Organization; string IdentityKey; Guid? GroupId; Guid? SourceId; string? Notes; bool IsTentative; ReminderCadence Cadence; Guid? OwnerId; Guid? MergedIntoId; DateTime UpdatedAt; }`
  - `enum ReminderCadence { None, Monthly, Quarterly, HalfYearly, Yearly }` — `None` is the default (FR-46).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Tests;

public class IdentityKeyUnitTests
{
    [Fact]
    public void Email_wins_when_it_is_known()
        => Assert.Equal("anne.berg@microsoft.com",
            IdentityKey.For("Anne.Berg@Microsoft.com", "Anne Berg", "Microsoft Denmark"));

    [Fact]
    public void Name_and_organisation_are_the_fallback()
        => Assert.Equal("anne berg|microsoft denmark",
            IdentityKey.For(null, "Anne  Berg", "Microsoft Denmark"));

    [Fact]
    public void The_same_name_at_a_different_organisation_is_a_different_person()
        => Assert.NotEqual(IdentityKey.For(null, "Anne Berg", "Microsoft Denmark"),
                           IdentityKey.For(null, "Anne Berg", "Microsoft Norway"));
}

[Collection("postgres")]
public class IdentityKeyTests
{
    private readonly PostgresFixture _pg;
    public IdentityKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Two_profiles_cannot_share_an_identity_key()
    {
        await using var db = _pg.NewContext();
        db.MsProfiles.Add(MsProfile.Create("Nina Due", "nina.due@microsoft.com", null));
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.MsProfiles.Add(MsProfile.Create("Nina Due", "NINA.DUE@microsoft.com", null));
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Cadence_defaults_to_none()
    {
        await using var db = _pg.NewContext();
        var p = MsProfile.Create("Bo Larsen", null, "Microsoft Denmark");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        Assert.Equal(ReminderCadence.None, (await db.MsProfiles.FindAsync(p.Id))!.Cadence);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter IdentityKey`
Expected: FAIL — `IdentityKey` does not exist.

- [ ] **Step 3: Write the key and the entity**

`backend/src/MsRelationship.Api/Features/MsProfiles/IdentityKey.cs`:

```csharp
using System.Text.RegularExpressions;

namespace MsRelationship.Api.Features.MsProfiles;

/// One canonical key per Microsoft person, stored rather than derived at query
/// time (FR-18). Work e-mail where it is known; name + organisation otherwise,
/// because an Anne Berg at Microsoft Norway is not the Danish one.
public static partial class IdentityKey
{
    public static string For(string? email, string name, string? organization)
    {
        if (!string.IsNullOrWhiteSpace(email)) return Squash(email);
        return $"{Squash(name)}|{Squash(organization ?? "")}";
    }

    private static string Squash(string s) => Whitespace().Replace(s.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
```

`backend/src/MsRelationship.Api/Data/Entities/MsProfile.cs`:

```csharp
using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Data.Entities;

/// How often the owner is reminded to reach out. None is the default: a person
/// only enters the reminder loop when somebody deliberately puts them there,
/// or the monthly mail becomes a list nobody reads (FR-46).
public enum ReminderCadence { None, Monthly, Quarterly, HalfYearly, Yearly }

public class MsProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Title { get; set; }
    /// Empty until a real source fills it. Never generated or guessed (FR-29).
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Organization { get; set; }
    public required string IdentityKey { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? SourceId { get; set; }
    public string? Notes { get; set; }
    /// Carried over from a source that could not settle something about them.
    public bool IsTentative { get; set; }
    public ReminderCadence Cadence { get; set; } = ReminderCadence.None;
    public Guid? OwnerId { get; set; }
    /// Set when this profile has been merged away; see Task 13.
    public Guid? MergedIntoId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static MsProfile Create(string name, string? email, string? organization) => new()
    {
        Name = name,
        Email = email,
        Organization = organization,
        IdentityKey = Features.MsProfiles.IdentityKey.For(email, name, organization)
    };
}
```

In `AppDbContext`:

```csharp
public DbSet<MsProfile> MsProfiles => Set<MsProfile>();
```

and in `OnModelCreating`:

```csharp
b.Entity<MsProfile>().HasIndex(x => x.IdentityKey).IsUnique();
b.Entity<MsProfile>().Property(x => x.Cadence).HasConversion<string>();
```

- [ ] **Step 4: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add MsProfiles --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter IdentityKey
git add backend && git commit -m "feat: Microsoft profiles with a canonical identity key"
```
Expected: 5 passing tests.

---

### Task 7: Match before create

Nothing may create a Microsoft person without first being told whether one already exists (FR-19). Near-matches surface rather than merge silently.

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfileMatcher.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MatchTests.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `MsProfile`, `IdentityKey`.
- Produces: `MsProfileMatcher.MatchAsync(string name, string? email, string? organization)` returning `MatchResult(MsProfile? Exact, IReadOnlyList<MsProfile> Suspected)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MatchTests
{
    private readonly PostgresFixture _pg;
    public MatchTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task An_exact_key_returns_the_existing_person()
    {
        await using var db = _pg.NewContext();
        var existing = MsProfile.Create("Liang Ye", "liang.ye@microsoft.com", null);
        db.MsProfiles.Add(existing);
        await db.SaveChangesAsync();

        var result = await new MsProfileMatcher(db).MatchAsync("Liang Ye", "Liang.Ye@microsoft.com", null);

        Assert.NotNull(result.Exact);
        Assert.Equal(existing.Id, result.Exact!.Id);
    }

    [Fact]
    public async Task The_same_name_without_an_email_is_suspected_not_assumed()
    {
        await using var db = _pg.NewContext();
        db.MsProfiles.Add(MsProfile.Create("Henrik Ditlevsen", "henrik.d@microsoft.com", null));
        await db.SaveChangesAsync();

        var result = await new MsProfileMatcher(db).MatchAsync("Henrik Ditlevsen", null, null);

        Assert.Null(result.Exact);
        Assert.Contains(result.Suspected, p => p.Name == "Henrik Ditlevsen");
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter MatchTests`
Expected: FAIL — `MsProfileMatcher` does not exist.

- [ ] **Step 3: Write the matcher**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MatchResult(MsProfile? Exact, IReadOnlyList<MsProfile> Suspected);

public class MsProfileMatcher(AppDbContext db)
{
    /// Surveys and imports let several people enter the same Microsoft person
    /// independently, with different spellings and often no e-mail. An exact key
    /// returns the existing row; a same-name-different-key is reported as
    /// suspected, for a human to resolve — never merged silently (FR-19).
    public async Task<MatchResult> MatchAsync(string name, string? email, string? organization)
    {
        var key = IdentityKey.For(email, name, organization);
        var exact = await db.MsProfiles
            .FirstOrDefaultAsync(p => p.IdentityKey == key && p.MergedIntoId == null);

        var normalised = name.Trim().ToLowerInvariant();
        var suspected = await db.MsProfiles
            .Where(p => p.MergedIntoId == null && p.IdentityKey != key && p.Name.ToLower() == normalised)
            .ToListAsync();

        return new MatchResult(exact, suspected);
    }
}
```

Register it: `builder.Services.AddScoped<MsProfileMatcher>();`

- [ ] **Step 4: Run, then commit**

```bash
dotnet test backend/tests/MsRelationship.Api.Tests --filter MatchTests
git add backend && git commit -m "feat: match an existing Microsoft person before creating one"
```

---

### Task 8: Domain membership and customers

Two many-to-many links. Domain membership is what puts a person on a board, and being in domains owned by two dashboards is exactly how a person appears on both (FR-24). Customers are records of their own so "who at Microsoft touches Novo?" is answerable (FR-49).

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsProfileDomain.cs`, `Customer.cs`, `MsProfileCustomer.cs`, `CustomerType.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MembershipTests.cs`

**Interfaces:**
- Consumes: `MsProfile`, `Domain`, `Dashboard`.
- Produces: `MsProfileDomain { Guid MsProfileId; Guid DomainId; }`, `Customer { Guid Id; string Name; CustomerType Type; }`, `MsProfileCustomer { Guid MsProfileId; Guid CustomerId; }`, `enum CustomerType { Unknown, Customer, Prospect }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MembershipTests
{
    private readonly PostgresFixture _pg;
    public MembershipTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_person_in_domains_from_two_dashboards_appears_on_both()
    {
        await using var db = _pg.NewContext();
        var a = new Dashboard { Slug = "data-ai", Label = "Data & AI" };
        var bd = new Dashboard { Slug = "dynamics", Label = "Dynamics" };
        var da = new Domain { DashboardId = a.Id, Name = "Partner Leadership" };
        var dbn = new Domain { DashboardId = bd.Id, Name = "Partner & Alliance" };
        var nina = MsProfile.Create("Nina Due", "nina.due@ms.example", null);
        db.AddRange(a, bd, da, dbn, nina);
        db.MsProfileDomains.AddRange(
            new MsProfileDomain { MsProfileId = nina.Id, DomainId = da.Id },
            new MsProfileDomain { MsProfileId = nina.Id, DomainId = dbn.Id });
        await db.SaveChangesAsync();

        var boards = await db.MsProfileDomains
            .Where(x => x.MsProfileId == nina.Id)
            .Join(db.Domains, x => x.DomainId, d => d.Id, (x, d) => d.DashboardId)
            .Distinct().ToListAsync();

        Assert.Equal(2, boards.Count);
    }

    [Fact]
    public async Task A_customer_is_a_record_so_it_can_be_queried_backwards()
    {
        await using var db = _pg.NewContext();
        var novo = new Customer { Name = "NOVO NORDISK" };
        var seller = MsProfile.Create("Jesper Pedersen", "jesper.p@ms.example", null);
        db.AddRange(novo, seller);
        db.MsProfileCustomers.Add(new MsProfileCustomer { MsProfileId = seller.Id, CustomerId = novo.Id });
        await db.SaveChangesAsync();

        var whoTouchesNovo = await db.MsProfileCustomers
            .Where(x => x.CustomerId == novo.Id)
            .Join(db.MsProfiles, x => x.MsProfileId, p => p.Id, (x, p) => p.Name)
            .ToListAsync();

        Assert.Equal(["Jesper Pedersen"], whoTouchesNovo);
    }

    [Fact]
    public async Task A_customer_type_defaults_to_unknown()
    {
        await using var db = _pg.NewContext();
        var c = new Customer { Name = "Carlsberg Group" };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        Assert.Equal(CustomerType.Unknown, (await db.Customers.FindAsync(c.Id))!.Type);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter MembershipTests`
Expected: FAIL — `MsProfileDomain` does not exist.

- [ ] **Step 3: Write the entities**

```csharp
namespace MsRelationship.Api.Data.Entities;

/// Membership runs through domains, so belonging to two boards needs no second
/// field to keep in sync (FR-24).
public class MsProfileDomain
{
    public Guid MsProfileId { get; set; }
    public Guid DomainId { get; set; }
}

/// Columbus customer, prospect, or not yet established. Unknown is the default:
/// the source material colours some names, but too irregularly to read (FR-50).
public enum CustomerType { Unknown, Customer, Prospect }

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public CustomerType Type { get; set; } = CustomerType.Unknown;
}

public class MsProfileCustomer
{
    public Guid MsProfileId { get; set; }
    public Guid CustomerId { get; set; }
}
```

In `AppDbContext`:

```csharp
public DbSet<MsProfileDomain> MsProfileDomains => Set<MsProfileDomain>();
public DbSet<Customer> Customers => Set<Customer>();
public DbSet<MsProfileCustomer> MsProfileCustomers => Set<MsProfileCustomer>();
```

and in `OnModelCreating`:

```csharp
b.Entity<MsProfileDomain>().HasKey(x => new { x.MsProfileId, x.DomainId });
b.Entity<MsProfileCustomer>().HasKey(x => new { x.MsProfileId, x.CustomerId });
b.Entity<Customer>().HasIndex(x => x.Name).IsUnique();
b.Entity<Customer>().Property(x => x.Type).HasConversion<string>();
```

- [ ] **Step 4: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add MembershipAndCustomers --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter MembershipTests
git add backend && git commit -m "feat: domain membership and customers, both many-to-many"
```

---

### Task 9: Relations

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/Relation.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/RelationTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser`, `MsProfile`.
- Produces: `Relation { Guid Id; Guid ColumbusUserId; Guid MsProfileId; short Score; string? Note; DateTime UpdatedAt; }` with `CHECK (score BETWEEN -3 AND 3)` and a unique index on the pair.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationTests
{
    private readonly PostgresFixture _pg;
    public RelationTests(PostgresFixture pg) => _pg = pg;

    private static async Task<(ColumbusUser, MsProfile)> Pair(AppDbContext db, string tag)
    {
        var u = new ColumbusUser { Name = $"User {tag}", Email = $"{tag}@columbusglobal.example" };
        var p = MsProfile.Create($"MS {tag}", $"{tag}@ms.example", null);
        db.AddRange(u, p);
        await db.SaveChangesAsync();
        return (u, p);
    }

    [Theory]
    [InlineData((short)-4)]
    [InlineData((short)4)]
    public async Task A_score_outside_the_scale_is_refused(short score)
    {
        await using var db = _pg.NewContext();
        var (u, p) = await Pair(db, Guid.NewGuid().ToString("N")[..8]);
        db.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = score });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_person_may_know_many_and_be_known_by_many()
    {
        await using var db = _pg.NewContext();
        var (u1, p1) = await Pair(db, "aa" + Guid.NewGuid().ToString("N")[..6]);
        var (u2, p2) = await Pair(db, "bb" + Guid.NewGuid().ToString("N")[..6]);
        db.Relations.AddRange(
            new Relation { ColumbusUserId = u1.Id, MsProfileId = p1.Id, Score = 3 },
            new Relation { ColumbusUserId = u1.Id, MsProfileId = p2.Id, Score = 1 },
            new Relation { ColumbusUserId = u2.Id, MsProfileId = p1.Id, Score = -1 });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Relations.CountAsync(r => r.ColumbusUserId == u1.Id));
        Assert.Equal(2, await db.Relations.CountAsync(r => r.MsProfileId == p1.Id));
    }

    [Fact]
    public async Task The_same_pair_cannot_be_registered_twice()
    {
        await using var db = _pg.NewContext();
        var (u, p) = await Pair(db, "cc" + Guid.NewGuid().ToString("N")[..6]);
        db.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = 2 });
        await db.SaveChangesAsync();

        await using var other = _pg.NewContext();
        other.Relations.Add(new Relation { ColumbusUserId = u.Id, MsProfileId = p.Id, Score = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter RelationTests`
Expected: FAIL — `Relation` does not exist.

- [ ] **Step 3: Write the entity and its constraints**

```csharp
namespace MsRelationship.Api.Data.Entities;

/// One Columbus person's view of one Microsoft person. The score is the whole
/// scale: -3 to +3, and nothing else (FR-01).
public class Relation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public short Score { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

In `AppDbContext`:

```csharp
public DbSet<Relation> Relations => Set<Relation>();
```

and in `OnModelCreating`:

```csharp
b.Entity<Relation>(e =>
{
    e.HasIndex(x => new { x.ColumbusUserId, x.MsProfileId }).IsUnique();
    e.ToTable(t => t.HasCheckConstraint("ck_relations_score_range", "score BETWEEN -3 AND 3"));
});
```

The check lives in the database rather than only in C#: an import or a console session is a write path too.

- [ ] **Step 4: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add Relations --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter RelationTests
git add backend && git commit -m "feat: relations with the score constraint and one row per pair"
```
Expected: 4 passing tests.

---

### Task 10: The acting user

History needs a name against every change (FR-32), and Stage 1 has no Entra yet. A stub supplies the acting user now; Entra replaces the stub later without touching a single call site, because callers only ever see `ICurrentUser`.

**Files:**
- Create: `backend/src/MsRelationship.Api/Auth/ICurrentUser.cs`, `Auth/DevUserMiddleware.cs`
- Modify: `backend/src/MsRelationship.Api/Program.cs`, `.env.example`
- Create: `backend/tests/MsRelationship.Api.Tests/DevUserTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser`.
- Produces: `ICurrentUser { Guid? Id; string? Email; bool IsSignedIn; }`, registered scoped. Task 11 and Task 12 depend on it.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MsRelationship.Api.Tests;

public class DevUserTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DevUserTests(WebApplicationFactory<Program> f) => _factory = f;

    [Fact]
    public async Task Without_the_header_nobody_is_signed_in()
    {
        var res = await _factory.CreateClient().GetAsync("/api/dev/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_header_names_the_acting_user()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "seeded@columbusglobal.example");
        var res = await client.GetAsync("/api/dev/whoami");
        res.EnsureSuccessStatusCode();
        Assert.Contains("seeded@columbusglobal.example", await res.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter DevUserTests`
Expected: FAIL — 404, the route does not exist.

- [ ] **Step 3: Write the abstraction and the stub**

`backend/src/MsRelationship.Api/Auth/ICurrentUser.cs`:

```csharp
namespace MsRelationship.Api.Auth;

/// Who is acting. Every write records this (FR-32). Stage 1 fills it from a
/// header; Entra fills it from a token later. Nothing above this line changes.
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Email { get; }
    bool IsSignedIn { get; }
}

public class CurrentUser : ICurrentUser
{
    public Guid? Id { get; set; }
    public string? Email { get; set; }
    public bool IsSignedIn => Id is not null;
}
```

`backend/src/MsRelationship.Api/Auth/DevUserMiddleware.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Auth;

/// Development only. Reads X-Dev-User and resolves it to a Columbus user, so
/// there is an author on every change before Entra exists. Mapped only when
/// DEV_AUTH is on, so it cannot be reached by accident in any other environment.
public class DevUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, CurrentUser current)
    {
        var email = ctx.Request.Headers["X-Dev-User"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await db.ColumbusUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user is not null) { current.Id = user.Id; current.Email = user.Email; }
        }
        await next(ctx);
    }
}
```

In `Program.cs`:

```csharp
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

var devAuth = builder.Configuration.GetValue("DEV_AUTH", builder.Environment.IsDevelopment());
...
if (devAuth)
{
    app.UseMiddleware<DevUserMiddleware>();
    app.MapGet("/api/dev/whoami", (ICurrentUser me) =>
        me.IsSignedIn ? Results.Ok(new { me.Id, me.Email }) : Results.Unauthorized());
}
```

Add `DEV_AUTH=true` to `.env.example` with a comment that it must never be true outside development.

- [ ] **Step 4: Create the two test helpers the later tasks rely on**

Create `FakeCurrentUser.cs` and `Fixtures.cs` exactly as given under **Test Helpers** at the end of
this plan. Tasks 11, 12 and 13 call `Fixtures.Trio(db)` and `new FakeCurrentUser(id)`, so they must
exist from here on.

- [ ] **Step 5: Make the acting user exist for the endpoint test**

`DevUserTests` needs a real row to resolve the header against. Boot the factory against the test
container and insert it:

```csharp
private WebApplicationFactory<Program> Factory(PostgresFixture pg) =>
    _factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", pg.ConnectionString)
                                      .UseSetting("DEV_AUTH", "true"));

// in the test, before the request:
await using var db = pg.NewContext();
if (!await db.ColumbusUsers.AnyAsync(u => u.Email == "seeded@columbusglobal.example"))
{
    db.ColumbusUsers.Add(new ColumbusUser { Name = "Seeded", Email = "seeded@columbusglobal.example" });
    await db.SaveChangesAsync();
}
```

Make `DevUserTests` join the `postgres` collection so it shares the container.

- [ ] **Step 6: Run and commit**

```bash
dotnet test backend/tests/MsRelationship.Api.Tests --filter DevUserTests
git add backend .env.example && git commit -m "feat: ICurrentUser with a development stub behind DEV_AUTH"
```
Expected: 2 passing tests.

---

### Task 11: The relation writer and the append-only history

The load-bearing task. Every change to a relationship goes through one writer that appends to history in the same transaction — and history has no update or delete path at all (FR-32, NFR-07).

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/RelationHistory.cs`, `RelationChangeType.cs`
- Create: `backend/src/MsRelationship.Api/Features/Relations/RelationWriter.cs`
- Modify: `AppDbContext.cs`, `Program.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/RelationWriterTests.cs`, `HistoryAppendOnlyTests.cs`

**Interfaces:**
- Consumes: `Relation`, `ICurrentUser`.
- Produces: `RelationWriter.SetAsync(Guid columbusUserId, Guid msProfileId, short score, string? note)` and `RelationWriter.RemoveAsync(Guid columbusUserId, Guid msProfileId)`, both returning `Task<Relation?>`. Task 13 and Task 14 call these rather than touching `db.Relations`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationWriterTests
{
    private readonly PostgresFixture _pg;
    public RelationWriterTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Creating_a_relation_writes_one_history_row_naming_the_actor()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 2, "Met at the partner day.");

        var history = await db.RelationHistory
            .Where(h => h.MsProfileId == p.Id).SingleAsync();
        Assert.Equal(RelationChangeType.Created, history.ChangeType);
        Assert.Null(history.OldScore);
        Assert.Equal((short)2, history.NewScore);
        Assert.Equal(actor.Id, history.ChangedByUserId);
    }

    [Fact]
    public async Task Changing_a_score_records_both_sides_of_the_change()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 1, null);
        await writer.SetAsync(u.Id, p.Id, 3, "Now a first call.");

        var latest = await db.RelationHistory
            .Where(h => h.MsProfileId == p.Id)
            .OrderByDescending(h => h.ChangedAt).FirstAsync();
        Assert.Equal((short)1, latest.OldScore);
        Assert.Equal((short)3, latest.NewScore);
        Assert.Equal(RelationChangeType.Updated, latest.ChangeType);
    }

    [Fact]
    public async Task Removing_a_relation_leaves_the_history_behind()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));

        await writer.SetAsync(u.Id, p.Id, 2, null);
        await writer.RemoveAsync(u.Id, p.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == p.Id));
        Assert.Equal(2, await db.RelationHistory.CountAsync(h => h.MsProfileId == p.Id));
    }

    [Fact]
    public async Task A_write_with_nobody_signed_in_is_refused()
    {
        await using var db = _pg.NewContext();
        var (_, u, p) = await Fixtures.Trio(db);
        var writer = new RelationWriter(db, new FakeCurrentUser(null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.SetAsync(u.Id, p.Id, 2, null));
    }
}
```

`HistoryAppendOnlyTests.cs` — the structural guarantee:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class HistoryAppendOnlyTests
{
    private readonly PostgresFixture _pg;
    public HistoryAppendOnlyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task History_rows_cannot_be_edited_or_deleted_through_the_context()
    {
        await using var db = _pg.NewContext();
        // Append-only is enforced by the context, not by remembering not to call Remove.
        var row = new RelationHistory
        {
            ColumbusUserId = Guid.NewGuid(), MsProfileId = Guid.NewGuid(),
            NewScore = 1, ChangeType = RelationChangeType.Created,
            ChangedByUserId = Guid.NewGuid()
        };
        db.RelationHistory.Add(row);
        await db.SaveChangesAsync();

        db.RelationHistory.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter "RelationWriterTests|HistoryAppendOnlyTests"`
Expected: FAIL — `RelationWriter` does not exist.

- [ ] **Step 3: Write the history entity**

```csharp
namespace MsRelationship.Api.Data.Entities;

public enum RelationChangeType { Created, Updated, Removed }

/// Append-only. One row per change, with the value before and after, so
/// "who changed this, when, and what did it say before" is answerable without
/// reading application logs (NFR-07). Nothing updates or deletes these rows.
public class RelationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public short? OldScore { get; set; }
    public short? NewScore { get; set; }
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public RelationChangeType ChangeType { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
```

In `AppDbContext`, make append-only structural rather than a convention:

```csharp
public DbSet<RelationHistory> RelationHistory => Set<RelationHistory>();

public override int SaveChanges() { GuardHistory(); return base.SaveChanges(); }
public override Task<int> SaveChangesAsync(CancellationToken ct = default)
{ GuardHistory(); return base.SaveChangesAsync(ct); }

/// The history table is the record of what happened. Editing it would make it a
/// record of what someone wanted to have happened, so the context refuses.
private void GuardHistory()
{
    foreach (var entry in ChangeTracker.Entries<RelationHistory>())
        if (entry.State is EntityState.Modified or EntityState.Deleted)
            throw new InvalidOperationException(
                "relation_history is append-only: rows may be added, never changed or removed.");
}
```

- [ ] **Step 4: Write the writer**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Relations;

/// The only way in and out of the relations table. Everything else calls this,
/// so a change without a history row is not something anyone has to remember
/// not to write — there is no other code path that could (FR-32).
public class RelationWriter(AppDbContext db, ICurrentUser me)
{
    public async Task<Relation?> SetAsync(Guid columbusUserId, Guid msProfileId, short score, string? note)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A change needs somebody to attribute it to.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var existing = await db.Relations
            .FirstOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);

        var history = new RelationHistory
        {
            ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
            OldScore = existing?.Score, OldNote = existing?.Note,
            NewScore = score, NewNote = note,
            ChangeType = existing is null ? RelationChangeType.Created : RelationChangeType.Updated,
            ChangedByUserId = actor
        };

        if (existing is null)
        {
            existing = new Relation
            {
                ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
                Score = score, Note = note
            };
            db.Relations.Add(existing);
        }
        else
        {
            existing.Score = score;
            existing.Note = note;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        db.RelationHistory.Add(history);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return existing;
    }

    public async Task<Relation?> RemoveAsync(Guid columbusUserId, Guid msProfileId)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A change needs somebody to attribute it to.");

        var existing = await db.Relations
            .FirstOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);
        if (existing is null) return null;   // removing what is not there is a no-op, not an error

        await using var tx = await db.Database.BeginTransactionAsync();
        db.RelationHistory.Add(new RelationHistory
        {
            ColumbusUserId = columbusUserId, MsProfileId = msProfileId,
            OldScore = existing.Score, OldNote = existing.Note,
            NewScore = null, NewNote = null,
            ChangeType = RelationChangeType.Removed,
            ChangedByUserId = actor
        });
        db.Relations.Remove(existing);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return existing;
    }
}
```

Register it: `builder.Services.AddScoped<RelationWriter>();`

- [ ] **Step 5: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add RelationHistory --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter "RelationWriterTests|HistoryAppendOnlyTests"
git add backend && git commit -m "feat: relation writer with append-only history, enforced by the context"
```
Expected: 5 passing tests.

---

### Task 12: The owner, the cadence and the contact log

The upkeep half (FR-43 to FR-46). The rule worth the most care: an owner must already hold a relation to the person they own.

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/ContactEntry.cs`
- Create: `backend/src/MsRelationship.Api/Features/Contacts/ContactService.cs`
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/OwnerService.cs`
- Modify: `AppDbContext.cs`, `Program.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/OwnerTests.cs`, `ContactLogTests.cs`

**Interfaces:**
- Consumes: `MsProfile.OwnerId`, `MsProfile.Cadence` (both from Task 6), `Relation`, `ICurrentUser`.
- Produces:
  - `ContactEntry { Guid Id; Guid MsProfileId; Guid RegisteredByUserId; DateOnly ContactedOn; DateTime CreatedAt; }`
  - `OwnerService.SetOwnerAsync(Guid msProfileId, Guid? ownerId)` returning `Task<bool>` — false when the candidate holds no relation.
  - `ContactService.RegisterAsync(Guid msProfileId, DateOnly? on = null)` returning `Task<ContactEntry>`, and `LastContactAsync(Guid msProfileId)` returning `Task<ContactEntry?>` — **null when the log is empty**, which the frontend renders as Unknown (FR-45).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Contacts;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class OwnerTests
{
    private readonly PostgresFixture _pg;
    public OwnerTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Somebody_holding_a_relation_may_be_made_owner()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 2, null);

        Assert.True(await new OwnerService(db).SetOwnerAsync(p.Id, u.Id));
        Assert.Equal(u.Id, (await db.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }

    [Fact]
    public async Task Somebody_with_no_relation_cannot_be_made_owner()
    {
        await using var db = _pg.NewContext();
        var (_, u, p) = await Fixtures.Trio(db);

        Assert.False(await new OwnerService(db).SetOwnerAsync(p.Id, u.Id));
        Assert.Null((await db.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }

    [Fact]
    public async Task An_owner_can_be_cleared()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 1, null);
        await new OwnerService(db).SetOwnerAsync(p.Id, u.Id);

        Assert.True(await new OwnerService(db).SetOwnerAsync(p.Id, null));
        Assert.Null((await db.MsProfiles.FindAsync(p.Id))!.OwnerId);
    }
}

[Collection("postgres")]
public class ContactLogTests
{
    private readonly PostgresFixture _pg;
    public ContactLogTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task An_empty_log_answers_null_not_a_date()
    {
        await using var db = _pg.NewContext();
        var (actor, _, p) = await Fixtures.Trio(db);
        var svc = new ContactService(db, new FakeCurrentUser(actor.Id));

        Assert.Null(await svc.LastContactAsync(p.Id));
    }

    [Fact]
    public async Task Anybody_may_register_a_contact_not_only_the_owner()
    {
        await using var db = _pg.NewContext();
        var (actor, u, p) = await Fixtures.Trio(db);
        await new RelationWriter(db, new FakeCurrentUser(actor.Id))
            .SetAsync(u.Id, p.Id, 2, null);
        await new OwnerService(db).SetOwnerAsync(p.Id, u.Id);

        // actor is not the owner, and registers anyway
        var entry = await new ContactService(db, new FakeCurrentUser(actor.Id)).RegisterAsync(p.Id);

        Assert.Equal(actor.Id, entry.RegisteredByUserId);
    }

    [Fact]
    public async Task The_newest_entry_is_the_last_contact_and_nothing_resets()
    {
        await using var db = _pg.NewContext();
        var (actor, _, p) = await Fixtures.Trio(db);
        var svc = new ContactService(db, new FakeCurrentUser(actor.Id));

        await svc.RegisterAsync(p.Id, new DateOnly(2026, 5, 4));
        await svc.RegisterAsync(p.Id, new DateOnly(2026, 8, 19));

        var last = await svc.LastContactAsync(p.Id);
        Assert.Equal(new DateOnly(2026, 8, 19), last!.ContactedOn);
        Assert.Equal(2, await db.ContactEntries.CountAsync(c => c.MsProfileId == p.Id));
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter "OwnerTests|ContactLogTests"`
Expected: FAIL — `OwnerService` does not exist.

- [ ] **Step 3: Write the entity and the two services**

```csharp
namespace MsRelationship.Api.Data.Entities;

/// One line per time somebody reached out. Date and who wrote it down, and
/// nothing else — the point is only to know when contact last happened (FR-44).
public class ContactEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MsProfileId { get; set; }
    public Guid RegisteredByUserId { get; set; }
    public DateOnly ContactedOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public class OwnerService(AppDbContext db)
{
    /// An owner must already hold a relation to the person (FR-43). Owning
    /// somebody nobody has spoken to would be a title, not a job — so a profile
    /// nobody knows simply has no owner, and shows up as the gap it is.
    public async Task<bool> SetOwnerAsync(Guid msProfileId, Guid? ownerId)
    {
        var profile = await db.MsProfiles.FindAsync(msProfileId);
        if (profile is null) return false;

        if (ownerId is not null)
        {
            var holdsRelation = await db.Relations
                .AnyAsync(r => r.MsProfileId == msProfileId && r.ColumbusUserId == ownerId);
            if (!holdsRelation) return false;
        }

        profile.OwnerId = ownerId;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Auth;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Contacts;

public class ContactService(AppDbContext db, ICurrentUser me)
{
    /// Anyone signed in may register a contact, not only the owner: it is a fact
    /// about the relationship, not the owner's property (FR-44).
    public async Task<ContactEntry> RegisterAsync(Guid msProfileId, DateOnly? on = null)
    {
        var actor = me.Id ?? throw new InvalidOperationException("A contact needs somebody to attribute it to.");
        var entry = new ContactEntry
        {
            MsProfileId = msProfileId,
            RegisteredByUserId = actor,
            ContactedOn = on ?? DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.ContactEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    /// Null when nothing is logged. Null means Unknown, not Never: an empty log
    /// means nobody wrote it down, not that nobody called (FR-45).
    public Task<ContactEntry?> LastContactAsync(Guid msProfileId) =>
        db.ContactEntries.Where(c => c.MsProfileId == msProfileId)
            .OrderByDescending(c => c.ContactedOn).ThenByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
}
```

In `AppDbContext`:

```csharp
public DbSet<ContactEntry> ContactEntries => Set<ContactEntry>();
```

Register both services in `Program.cs`:

```csharp
builder.Services.AddScoped<OwnerService>();
builder.Services.AddScoped<ContactService>();
```

- [ ] **Step 4: Migrate, run, commit**

```bash
cd backend && dotnet ef migrations add ContactLog --project src/MsRelationship.Api
dotnet test tests/MsRelationship.Api.Tests --filter "OwnerTests|ContactLogTests"
git add backend && git commit -m "feat: relationship owner, cadence and the contact log"
```
Expected: 6 passing tests.

---

### Task 13: Merging duplicates

When two rows turn out to be the same person, everything they carry has to move (FR-20) — relations, contacts, customers, domain membership and history. This is the task most worth writing tests for first.

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfileMerger.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MergeTests.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `MsProfileMerger.MergeAsync(Guid survivorId, Guid duplicateId)` returning `Task<MergeResult(bool Merged, string? Refused)>`. The duplicate is kept as a tombstone with `MergedIntoId` set, so an old link still resolves.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MergeTests
{
    private readonly PostgresFixture _pg;
    public MergeTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Relations_contacts_and_customers_move_to_the_survivor()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Anne Berg", null, "Microsoft Denmark");
        var customer = new Customer { Name = "Hempel A/S " + Guid.NewGuid().ToString("N")[..6] };
        db.AddRange(duplicate, customer);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));
        await writer.SetAsync(u.Id, duplicate.Id, 2, "knows them well");
        db.ContactEntries.Add(new ContactEntry
        {
            MsProfileId = duplicate.Id, RegisteredByUserId = actor.Id,
            ContactedOn = new DateOnly(2026, 6, 1)
        });
        db.MsProfileCustomers.Add(new MsProfileCustomer
        {
            MsProfileId = duplicate.Id, CustomerId = customer.Id
        });
        await db.SaveChangesAsync();

        var result = await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

        Assert.True(result.Merged);
        Assert.True(await db.Relations.AnyAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id));
        Assert.True(await db.ContactEntries.AnyAsync(c => c.MsProfileId == survivor.Id));
        Assert.True(await db.MsProfileCustomers.AnyAsync(c => c.MsProfileId == survivor.Id));
        Assert.Equal(survivor.Id, (await db.MsProfiles.FindAsync(duplicate.Id))!.MergedIntoId);
    }

    [Fact]
    public async Task When_both_held_the_same_pair_the_newer_relation_survives()
    {
        await using var db = _pg.NewContext();
        var (actor, u, survivor) = await Fixtures.Trio(db);
        var duplicate = MsProfile.Create("Bo Larsen", null, "Microsoft Denmark " + Guid.NewGuid());
        db.Add(duplicate);
        await db.SaveChangesAsync();

        var writer = new RelationWriter(db, new FakeCurrentUser(actor.Id));
        await writer.SetAsync(u.Id, survivor.Id, 0, "older");
        await Task.Delay(10);
        await writer.SetAsync(u.Id, duplicate.Id, 3, "newer");

        await new MsProfileMerger(db).MergeAsync(survivor.Id, duplicate.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == u.Id);
        Assert.Equal((short)3, kept.Score);
        // the losing side is not lost, only superseded
        Assert.True(await db.RelationHistory.AnyAsync(h => h.MsProfileId == survivor.Id));
    }

    [Fact]
    public async Task A_profile_cannot_be_merged_into_itself()
    {
        await using var db = _pg.NewContext();
        var (_, _, p) = await Fixtures.Trio(db);
        var result = await new MsProfileMerger(db).MergeAsync(p.Id, p.Id);
        Assert.False(result.Merged);
        Assert.NotNull(result.Refused);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter MergeTests`
Expected: FAIL — `MsProfileMerger` does not exist.

- [ ] **Step 3: Write the merger**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public record MergeResult(bool Merged, string? Refused);

public class MsProfileMerger(AppDbContext db)
{
    /// Everything the duplicate carried moves to the survivor. Where both held a
    /// relation from the same Columbus person, the more recently updated one
    /// wins and the other stays in history — a merge must not quietly discard
    /// somebody's assessment (FR-20).
    public async Task<MergeResult> MergeAsync(Guid survivorId, Guid duplicateId)
    {
        if (survivorId == duplicateId) return new MergeResult(false, "A profile cannot be merged into itself.");

        var survivor = await db.MsProfiles.FindAsync(survivorId);
        var duplicate = await db.MsProfiles.FindAsync(duplicateId);
        if (survivor is null || duplicate is null) return new MergeResult(false, "Profile not found.");
        if (duplicate.MergedIntoId is not null) return new MergeResult(false, "Already merged.");

        await using var tx = await db.Database.BeginTransactionAsync();

        var duplicateRelations = await db.Relations.Where(r => r.MsProfileId == duplicateId).ToListAsync();
        foreach (var rel in duplicateRelations)
        {
            var clash = await db.Relations.FirstOrDefaultAsync(
                r => r.MsProfileId == survivorId && r.ColumbusUserId == rel.ColumbusUserId);
            if (clash is null) { rel.MsProfileId = survivorId; continue; }

            if (rel.UpdatedAt > clash.UpdatedAt) { clash.Score = rel.Score; clash.Note = rel.Note; clash.UpdatedAt = rel.UpdatedAt; }
            db.Relations.Remove(rel);
        }

        await db.ContactEntries.Where(c => c.MsProfileId == duplicateId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MsProfileId, survivorId));
        await db.RelationHistory.Where(h => h.MsProfileId == duplicateId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.MsProfileId, survivorId));

        foreach (var link in await db.MsProfileCustomers.Where(x => x.MsProfileId == duplicateId).ToListAsync())
        {
            var exists = await db.MsProfileCustomers.AnyAsync(
                x => x.MsProfileId == survivorId && x.CustomerId == link.CustomerId);
            db.MsProfileCustomers.Remove(link);
            if (!exists) db.MsProfileCustomers.Add(new Data.Entities.MsProfileCustomer
            { MsProfileId = survivorId, CustomerId = link.CustomerId });
        }

        foreach (var link in await db.MsProfileDomains.Where(x => x.MsProfileId == duplicateId).ToListAsync())
        {
            var exists = await db.MsProfileDomains.AnyAsync(
                x => x.MsProfileId == survivorId && x.DomainId == link.DomainId);
            db.MsProfileDomains.Remove(link);
            if (!exists) db.MsProfileDomains.Add(new Data.Entities.MsProfileDomain
            { MsProfileId = survivorId, DomainId = link.DomainId });
        }

        /* Kept as a tombstone rather than deleted, so a link or an import that
           still names the old id resolves to the survivor instead of 404ing. */
        duplicate.MergedIntoId = survivorId;
        duplicate.OwnerId = null;
        survivor.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new MergeResult(true, null);
    }
}
```

Note: `RelationHistory` is moved with `ExecuteUpdateAsync`, which bypasses the change tracker and therefore the append-only guard. That is deliberate and is the only sanctioned exception — re-pointing a row at the surviving profile does not change what it records. Leave a comment saying so at the call site.

- [ ] **Step 4: Run, then commit**

```bash
dotnet test backend/tests/MsRelationship.Api.Tests --filter MergeTests
git add backend && git commit -m "feat: merge duplicate Microsoft profiles, moving everything they carry"
```
Expected: 3 passing tests.

---

### Task 14: Seed the mockup data set and prove the stage

The exit criterion: the API holds the mockup's full data set, and every change is attributable.

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Seed/seed.json`, `Data/Seed/MockupSeeder.cs`
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfilesController.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/SeedTests.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: every entity and service above.
- Produces: `MockupSeeder.SeedAsync()`, idempotent; `GET /api/ms-profiles` returning name, title, dashboards, owner, last contact and score summary.

- [ ] **Step 1: Extract the seed from the mockup**

The mockup is the source of the data set, at tag `mockup-v1`. Extract `state.customers`, `state.domains`, `state.boards`, `state.msProfiles`, `state.columbusProfiles`, `state.relations` and `state.contacts` from `index.html` into `seed.json`, keeping the ids so the two can be compared row by row.

```bash
node -e '
  const fs = require("fs");
  const html = fs.readFileSync("index.html", "utf8");
  const start = html.indexOf("var state = {");
  const end = html.indexOf("\n};", start) + 2;
  const state = eval("(" + html.slice(start + 12, end) + ")");
  const { customers, contacts, boards, domains, msProfiles, columbusProfiles, relations } = state;
  fs.writeFileSync("backend/src/MsRelationship.Api/Data/Seed/seed.json",
    JSON.stringify({ customers, contacts, boards, domains, msProfiles, columbusProfiles, relations }, null, 1));
'
```

- [ ] **Step 2: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Seed;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SeedTests
{
    private readonly PostgresFixture _pg;
    public SeedTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task The_whole_mockup_data_set_loads()
    {
        await using var db = _pg.NewContext();
        await new MockupSeeder(db).SeedAsync();

        Assert.Equal(102, await db.MsProfiles.CountAsync());
        Assert.Equal(26, await db.ColumbusUsers.CountAsync());
        Assert.Equal(70, await db.Relations.CountAsync());
        Assert.Equal(70, await db.Customers.CountAsync());
        Assert.Equal(2, await db.Dashboards.CountAsync());
        Assert.Equal(17, await db.Domains.CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = _pg.NewContext();
        await new MockupSeeder(db).SeedAsync();
        var before = await db.MsProfiles.CountAsync();
        await new MockupSeeder(db).SeedAsync();
        Assert.Equal(before, await db.MsProfiles.CountAsync());
    }

    [Fact]
    public async Task Every_owner_in_the_seed_holds_a_relation_to_the_person_they_own()
    {
        await using var db = _pg.NewContext();
        await new MockupSeeder(db).SeedAsync();

        var broken = await db.MsProfiles
            .Where(p => p.OwnerId != null)
            .Where(p => !db.Relations.Any(r => r.MsProfileId == p.Id && r.ColumbusUserId == p.OwnerId))
            .Select(p => p.Name)
            .ToListAsync();

        Assert.Empty(broken);   // FR-43 holds for the seeded data too
    }
}
```

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test backend/tests/MsRelationship.Api.Tests --filter SeedTests`
Expected: FAIL — `MockupSeeder` does not exist.

- [ ] **Step 4: Write the seeder**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data.Seed;

/// Loads the mockup data set. Idempotent, and wrapped in one transaction: a
/// half-seeded database is worse than an empty one, because it looks like it
/// worked.
public class MockupSeeder(AppDbContext db)
{
    private record SeedFile(
        List<SeedCustomer> Customers, List<SeedContact> Contacts, List<SeedBoard> Boards,
        List<SeedDomain> Domains, List<SeedMsProfile> MsProfiles,
        List<SeedCbUser> ColumbusProfiles, List<SeedRelation> Relations);

    private record SeedCustomer(string Id, string Name, string Type);
    private record SeedContact(string Id, string MsProfileId, string ById, string Date);
    private record SeedBoard(string Id, string Label, string? Subtitle, string? Owner, string? Version, string? Updated, bool System);
    private record SeedDomain(string Id, string? Dept, string Name, string? Owner, string? Desc, bool System);
    private record SeedMsProfile(string Id, string Name, string? Title, string? Email, string? Phone,
        List<string> DomainIds, string? Group, string? Source, string? Notes, bool Tentative,
        string? OwnerId, string? Cadence, List<string>? CustomerIds);
    private record SeedCbUser(string Id, string Name, string? Title, string? Department, string Email, string? Phone, string Role);
    private record SeedRelation(string Id, string ColumbusId, string MsProfileId, short Score, string? Note);

    public async Task SeedAsync()
    {
        if (await db.MsProfiles.AnyAsync()) return;          // idempotent

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "seed.json");
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        await using var tx = await db.Database.BeginTransactionAsync();

        // Ids in the mockup are strings like "m14"; map them to the Guids used here.
        var boardId = new Dictionary<string, Guid>();
        var domainId = new Dictionary<string, Guid>();
        var userId = new Dictionary<string, Guid>();
        var profileId = new Dictionary<string, Guid>();
        var customerId = new Dictionary<string, Guid>();
        var groupId = new Dictionary<string, Guid>();
        var sourceId = new Dictionary<string, Guid>();
        var departmentId = new Dictionary<string, Guid>();

        Guid Taxonomy<T>(Dictionary<string, Guid> map, DbSet<T> set, string? name, Func<string, T> make) where T : class
        {
            name = string.IsNullOrWhiteSpace(name) ? "Unspecified" : name;
            if (map.TryGetValue(name, out var id)) return id;
            var entity = make(name);
            set.Add(entity);
            id = (Guid)typeof(T).GetProperty("Id")!.GetValue(entity)!;
            map[name] = id;
            return id;
        }

        foreach (var b in seed.Boards)
        {
            var board = new Dashboard
            {
                Slug = b.Id, Label = b.Label, Subtitle = b.Subtitle,
                Owner = b.Owner, Version = b.Version, UpdatedLabel = b.Updated, IsSystem = b.System
            };
            db.Dashboards.Add(board);
            boardId[b.Id] = board.Id;
        }

        foreach (var d in seed.Domains)
        {
            var domain = new Domain
            {
                DashboardId = d.Dept is not null && boardId.TryGetValue(d.Dept, out var bid) ? bid : null,
                Name = d.Name, Owner = d.Owner, Description = d.Desc, IsSystem = d.System
            };
            db.Domains.Add(domain);
            domainId[d.Id] = domain.Id;
        }

        foreach (var c in seed.Customers)
        {
            var customer = new Customer
            {
                Name = c.Name,
                Type = Enum.TryParse<CustomerType>(c.Type, true, out var t) ? t : CustomerType.Unknown
            };
            db.Customers.Add(customer);
            customerId[c.Id] = customer.Id;
        }

        foreach (var u in seed.ColumbusProfiles)
        {
            var user = new ColumbusUser
            {
                Name = u.Name, Title = u.Title, Email = u.Email, Phone = u.Phone,
                DepartmentId = Taxonomy(departmentId, db.CbDepartments, u.Department,
                    n => new CbDepartment { Name = n }),
                Role = u.Role switch
                {
                    "superadmin" => UserRole.SuperAdmin,
                    "admin" => UserRole.Admin,
                    _ => UserRole.Editor
                }
            };
            db.ColumbusUsers.Add(user);
            userId[u.Id] = user.Id;
        }

        foreach (var p in seed.MsProfiles)
        {
            var profile = MsProfile.Create(p.Name, string.IsNullOrWhiteSpace(p.Email) ? null : p.Email, "Microsoft Denmark");
            profile.Title = p.Title;
            profile.Phone = p.Phone;
            profile.Notes = p.Notes;
            profile.IsTentative = p.Tentative;
            profile.GroupId = Taxonomy(groupId, db.MsGroups, p.Group, n => new MsGroup { Name = n });
            profile.SourceId = Taxonomy(sourceId, db.MsSources, p.Source, n => new MsSource { Name = n });
            profile.Cadence = Enum.TryParse<ReminderCadence>(p.Cadence, true, out var c) ? c : ReminderCadence.None;
            db.MsProfiles.Add(profile);
            profileId[p.Id] = profile.Id;

            foreach (var d in p.DomainIds.Where(domainId.ContainsKey))
                db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = profile.Id, DomainId = domainId[d] });
            foreach (var cu in (p.CustomerIds ?? []).Where(customerId.ContainsKey))
                db.MsProfileCustomers.Add(new MsProfileCustomer { MsProfileId = profile.Id, CustomerId = customerId[cu] });
        }

        foreach (var r in seed.Relations.Where(r => userId.ContainsKey(r.ColumbusId) && profileId.ContainsKey(r.MsProfileId)))
            db.Relations.Add(new Relation
            {
                ColumbusUserId = userId[r.ColumbusId], MsProfileId = profileId[r.MsProfileId],
                Score = r.Score, Note = r.Note
            });

        foreach (var c in seed.Contacts.Where(c => profileId.ContainsKey(c.MsProfileId) && userId.ContainsKey(c.ById)))
            db.ContactEntries.Add(new ContactEntry
            {
                MsProfileId = profileId[c.MsProfileId], RegisteredByUserId = userId[c.ById],
                ContactedOn = DateOnly.Parse(c.Date)
            });

        await db.SaveChangesAsync();

        /* Owners are set last, because the rule is that an owner must already
           hold a relation — and the relations only exist once the line above
           has run (FR-43). */
        foreach (var p in seed.MsProfiles.Where(p => !string.IsNullOrEmpty(p.OwnerId)))
        {
            if (!userId.TryGetValue(p.OwnerId!, out var owner)) continue;
            var target = profileId[p.Id];
            if (!await db.Relations.AnyAsync(r => r.MsProfileId == target && r.ColumbusUserId == owner)) continue;
            (await db.MsProfiles.FindAsync(target))!.OwnerId = owner;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
```

Wire it into `Program.cs` behind its own switch:

```csharp
if (builder.Configuration.GetValue("SEED_MOCKUP", false))
{
    using var scope = app.Services.CreateScope();
    await new MockupSeeder(scope.ServiceProvider.GetRequiredService<AppDbContext>()).SeedAsync();
}
```

- [ ] **Step 5: Add the read endpoint that proves it**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

[ApiController]
[Route("api/ms-profiles")]
public class MsProfilesController(AppDbContext db) : ControllerBase
{
    /// Raw rows plus the two derived values the list needs. Statistics stay on
    /// the client, so the production numbers are the mockup's arithmetic on the
    /// mockup's shapes — see §4.4 of the plan.
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.MsProfiles
        .Where(p => p.MergedIntoId == null)
        .OrderBy(p => p.Name)
        .Select(p => new
        {
            p.Id, p.Name, p.Title, p.Email, p.Phone, p.Cadence, p.IsTentative,
            OwnerName = db.ColumbusUsers.Where(u => u.Id == p.OwnerId).Select(u => u.Name).FirstOrDefault(),
            LastContact = db.ContactEntries.Where(c => c.MsProfileId == p.Id)
                .OrderByDescending(c => c.ContactedOn).Select(c => (DateOnly?)c.ContactedOn).FirstOrDefault(),
            Dashboards = db.MsProfileDomains.Where(x => x.MsProfileId == p.Id)
                .Join(db.Domains, x => x.DomainId, d => d.Id, (x, d) => d.DashboardId)
                .Distinct().ToList(),
            Scores = db.Relations.Where(r => r.MsProfileId == p.Id).Select(r => r.Score).ToList()
        })
        .ToListAsync());
}
```

- [ ] **Step 6: Run the whole suite and commit**

```bash
dotnet test backend/tests/MsRelationship.Api.Tests
git add backend && git commit -m "feat: seed the mockup data set and expose the profile list"
```
Expected: every test in the project passes.

- [ ] **Step 7: Check the stage off against its exit criterion**

Run the API with `SEED_MOCKUP=true` and `DEV_AUTH=true`, then:

```bash
curl -s localhost:5080/api/ms-profiles -H "X-Dev-User: mette.kirkegaard@columbusglobal.example" | head -40
```

The stage is done when that returns the mockup's 102 people with their owners and last-contact dates, and `select count(*) from relation_history` is non-zero after any write.

---

## Test Helpers

Both used throughout; create them in Task 10 when `ICurrentUser` first exists.

`backend/tests/MsRelationship.Api.Tests/FakeCurrentUser.cs`:

```csharp
using MsRelationship.Api.Auth;

namespace MsRelationship.Api.Tests;

public class FakeCurrentUser(Guid? id) : ICurrentUser
{
    public Guid? Id { get; } = id;
    public string? Email { get; } = id is null ? null : $"{id}@columbusglobal.example";
    public bool IsSignedIn => Id is not null;
}
```

`backend/tests/MsRelationship.Api.Tests/Fixtures.cs`:

```csharp
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

public static class Fixtures
{
    /// An actor, a Columbus person and a Microsoft person, all unique per call so
    /// tests sharing one container cannot collide on a unique index.
    public static async Task<(ColumbusUser Actor, ColumbusUser User, MsProfile Profile)> Trio(AppDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var actor = new ColumbusUser { Name = $"Actor {tag}", Email = $"actor-{tag}@columbusglobal.example" };
        var user = new ColumbusUser { Name = $"User {tag}", Email = $"user-{tag}@columbusglobal.example" };
        var profile = MsProfile.Create($"MS {tag}", $"ms-{tag}@microsoft.example", null);
        db.AddRange(actor, user, profile);
        await db.SaveChangesAsync();
        return (actor, user, profile);
    }
}
```

---

## What this stage deliberately does not build

Named so nobody adds them on the way past:

- **No Entra.** `DEV_AUTH` and a header. Real sign-in is Stage 3's job, and `ICurrentUser` is the seam.
- **No undo, no recycle bin, no backups.** That is Stage 2, and it ships before anybody but the team can write.
- **No dashboards frontend, no panels, no eight-panel ceiling.** Panels are derived in the client (FR-23); the API only stores dashboards and domains.
- **No monthly reminder mail.** Stage 6. The cadence field exists; nothing reads it yet.
- **No soft delete.** Deletes are real in this stage; Stage 2 turns them soft. Do not scatter `IsDeleted` flags in advance.
- **No country column.** One instance, one country (TEC-04a).
