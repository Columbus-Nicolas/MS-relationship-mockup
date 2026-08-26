# Backend Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a working ASP.NET Core 9 API over Postgres that owns the relationship data model, enforces Microsoft-person identity, supports deduplication and merge, retains leavers' history, and gates every request behind Entra ID plus a closed membership list.

**Architecture:** Feature-folder ASP.NET Core Web API with EF Core 9 against Postgres 16. Taxonomies (groups, sources, departments, domains) are rows rather than enums, so they extend without migration — this is what NFR-01 asks for. All relationship mutations route through one `RelationWriter` service so the append-only `relation_history` table can never be bypassed. The API returns raw entities; statistics are computed in the frontend from ported mockup arithmetic (see spec §4).

**Tech Stack:** .NET 9, ASP.NET Core Web API, EF Core 9, Npgsql, Postgres 16 (`pg_trgm`), xUnit, Testcontainers.PostgreSql, Docker Compose, Mailpit.

**Spec:** `docs/superpowers/specs/2026-08-26-ms-relationship-app-design.md`

## Global Constraints

- .NET 9 / C# 13. EF Core 9. Npgsql provider. Postgres 16.
- API listens on `http://localhost:5080` — fixed by `VITE_API_BASE_URL` in the existing `.env`.
- Configuration keys follow the double-underscore convention already in `.env`: `AZUREAD__TENANTID`, `AZUREAD__CLIENTID`, `AZUREAD__AUDIENCE`, `AZUREAD__ALLOWEDEMAILDOMAINS`.
- `AZUREAD__ALLOWEDEMAILDOMAINS` is comma-separated. Empty means allow-any and is honoured **in Development only**.
- Relationship score is an integer in `[-3, 3]`, enforced by a database CHECK constraint.
- Taxonomies are rows. Never an enum, never a migration, for: Microsoft groups, Microsoft sources, Columbus departments, Microsoft domains.
- Microsoft profile rows are **tombstoned via `merged_into_id`, never deleted**.
- Columbus users are **archived via `status`, never deleted**.
- Every write to `relations` goes through `RelationWriter`. No controller touches `DbContext.Relations` directly.
- All backend code lives under `backend/`. The mockup `index.html` at the repo root is not modified by this plan.
- Migrations are checked in. Never edit a migration that has been committed.
- `ICurrentUser` is not defined until Task 13. Tasks 9–12 write controller endpoints that reference it. Until Task 13 lands, take the actor as an explicit `Guid actorId` parameter and swap it for `ICurrentUser` in Task 13 Step 6. The *services* those endpoints call already take `actorId` explicitly, so only the controller signatures are affected — and the tests, which call the services directly, are unaffected either way.

---

### Task 1: Repository hygiene and local infrastructure

`.env` is currently tracked in git with `POSTGRES_PASSWORD` and `PGADMIN_PASSWORD` in it (commit `015ba58`), and so is `.DS_Store`. This task stops the bleeding going forward. **It does not rewrite history** — the secrets remain in past commits, and purging them is the repo owner's decision (spec OD-6). Flag it and move on.

**Files:**
- Modify: `.gitignore`
- Create: `.env.example`
- Create: `docker-compose.yml`

- [ ] **Step 1: Extend `.gitignore`**

```gitignore
# Local tooling / dev-server config
.claude/

# Secrets — .env is tracked in history as of 015ba58; see spec OD-6
.env

# OS noise
.DS_Store

# .NET
bin/
obj/
*.user

# Node
node_modules/
dist/
```

- [ ] **Step 2: Untrack the two files without deleting them from disk**

```bash
git rm --cached .env .DS_Store
```

- [ ] **Step 3: Create `.env.example` as the documented template**

Copy `.env` verbatim, then blank every secret value. Keys must stay in the same order so a diff against a developer's real `.env` is readable.

```bash
sed -E 's/^(POSTGRES_PASSWORD|PGADMIN_PASSWORD)=.*/\1=/' .env > .env.example
```

- [ ] **Step 4: Create `docker-compose.yml`**

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

  mail:
    image: axllent/mailpit:latest
    ports:
      - "${MAILPIT_SMTP_PORT}:1025"
      - "${MAILPIT_UI_PORT}:8025"

  pgadmin:
    image: dpage/pgadmin4:latest
    profiles: ["tools"]
    environment:
      PGADMIN_DEFAULT_EMAIL: ${PGADMIN_EMAIL}
      PGADMIN_DEFAULT_PASSWORD: ${PGADMIN_PASSWORD}
    ports: ["${PGADMIN_PORT}:80"]

volumes:
  pgdata:
```

- [ ] **Step 5: Verify the stack comes up**

Run: `docker compose up -d db mail && docker compose ps`
Expected: `db` reports `healthy`, `mail` reports `running`.

- [ ] **Step 6: Verify `.env` is no longer staged**

Run: `git status --short`
Expected: `D  .env` and `D  .DS_Store` staged (index-only removal), and `.env` still present on disk via `ls .env`.

- [ ] **Step 7: Commit**

```bash
git add .gitignore .env.example docker-compose.yml
git commit -m "chore: untrack .env, add compose stack and env template"
```

---

### Task 2: Solution, API project, and the Postgres test fixture

**Files:**
- Create: `backend/MsRelationship.sln`
- Create: `backend/src/MsRelationship.Api/MsRelationship.Api.csproj`
- Create: `backend/src/MsRelationship.Api/Program.cs`
- Create: `backend/tests/MsRelationship.Api.Tests/MsRelationship.Api.Tests.csproj`
- Create: `backend/tests/MsRelationship.Api.Tests/PostgresFixture.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `PostgresFixture` — an xUnit collection fixture exposing `string ConnectionString` and `AppDbContext NewContext()`. Every later task's integration tests use it.

- [ ] **Step 1: Scaffold the solution and projects**

```bash
cd backend
dotnet new sln -n MsRelationship
dotnet new webapi -n MsRelationship.Api -o src/MsRelationship.Api --framework net9.0
dotnet new xunit -n MsRelationship.Api.Tests -o tests/MsRelationship.Api.Tests --framework net9.0
dotnet sln add src/MsRelationship.Api tests/MsRelationship.Api.Tests
dotnet add tests/MsRelationship.Api.Tests reference src/MsRelationship.Api
```

- [ ] **Step 2: Add packages**

```bash
cd backend
dotnet add src/MsRelationship.Api package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.*
dotnet add src/MsRelationship.Api package Microsoft.EntityFrameworkCore.Design --version 9.0.*
dotnet add tests/MsRelationship.Api.Tests package Testcontainers.PostgreSql
dotnet add tests/MsRelationship.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 3: Write the failing health test**

`backend/tests/MsRelationship.Api.Tests/HealthTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MsRelationship.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `cd backend && dotnet test --filter Health_returns_ok`
Expected: FAIL — `Program` is not accessible, or 404 on `/health`.

- [ ] **Step 5: Write `Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("ok"));
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can find it.
public partial class Program;
```

- [ ] **Step 6: Pin the API to port 5080**

`backend/src/MsRelationship.Api/Properties/launchSettings.json` — set the `applicationUrl` of the `http` profile to `http://localhost:5080`.

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd backend && dotnet test --filter Health_returns_ok`
Expected: PASS.

- [ ] **Step 8: Write the Postgres fixture**

`backend/tests/MsRelationship.Api.Tests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace MsRelationship.Api.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

This will not compile until Task 3 creates `AppDbContext`. That is expected and is why the commit below covers Task 2's health test only.

- [ ] **Step 9: Commit**

```bash
git add backend
git commit -m "feat: scaffold API solution with health endpoint and Postgres test fixture"
```

---

### Task 3: Taxonomy and domain tables

This is the task that discharges NFR-01. The proof is the test: a new Microsoft group becomes usable through an insert, with no migration and no restart.

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsGroup.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsSource.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/CbDepartment.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsDomain.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/TaxonomyTests.cs`

**Interfaces:**
- Consumes: `PostgresFixture` (Task 2).
- Produces: `AppDbContext` with `DbSet<MsGroup> MsGroups`, `DbSet<MsSource> MsSources`, `DbSet<CbDepartment> CbDepartments`, `DbSet<MsDomain> MsDomains`. Every entity has `Guid Id`. `MsDomain` additionally has `string Name`, `string Owner`, `string Description`, `bool IsSystem`, `int SortOrder`.

- [ ] **Step 1: Write the failing test**

`backend/tests/MsRelationship.Api.Tests/TaxonomyTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class TaxonomyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_new_group_needs_no_migration()
    {
        await using var db = fixture.NewContext();
        db.MsGroups.Add(new MsGroup { Id = Guid.NewGuid(), Name = "Customer Success" });
        await db.SaveChangesAsync();

        Assert.True(await db.MsGroups.AnyAsync(g => g.Name == "Customer Success"));
    }

    [Fact]
    public async Task Domain_names_are_unique()
    {
        await using var db = fixture.NewContext();
        db.MsDomains.Add(new MsDomain { Id = Guid.NewGuid(), Name = "Duplicated", Owner = "", Description = "" });
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.MsDomains.Add(new MsDomain { Id = Guid.NewGuid(), Name = "Duplicated", Owner = "", Description = "" });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter TaxonomyTests`
Expected: FAIL — `AppDbContext` and the entity types do not exist.

- [ ] **Step 3: Write the entities**

`backend/src/MsRelationship.Api/Data/Entities/MsGroup.cs`:

```csharp
namespace MsRelationship.Api.Data.Entities;

public class MsGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
```

`MsSource.cs` and `CbDepartment.cs` are the same shape with the class name changed. Write them out in full rather than aliasing.

`backend/src/MsRelationship.Api/Data/Entities/MsDomain.cs`:

```csharp
namespace MsRelationship.Api.Data.Entities;

public class MsDomain
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
}
```

`IsSystem` marks the `Unmarked` fallback domain, which the mockup refuses to edit or delete (`index.html:2727`).

- [ ] **Step 4: Write `AppDbContext`**

```csharp
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
```

- [ ] **Step 5: Register the context and create the migration**

In `Program.cs`, before `builder.Build()`:

```csharp
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

```bash
cd backend
dotnet ef migrations add Taxonomies --project src/MsRelationship.Api
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter TaxonomyTests`
Expected: PASS, both tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: taxonomy and domain tables, extensible without migration"
```

---

### Task 4: Columbus users

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/ColumbusUser.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/UserRole.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/UserStatus.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/ColumbusUserTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 3).
- Produces: `ColumbusUser { Guid Id; string? EntraObjectId; string Email; string Name; string Title; Guid? DepartmentId; string[] Skills; UserRole Role; UserStatus Status; DateTimeOffset? ArchivedAt; DateTimeOffset? LastSurveyAt; }`. `UserRole { Standard, Moderator, Admin, SuperAdmin }`. `UserStatus { Active, Archived }`. Both enums are stored as strings.

Role and status are enums rather than taxonomy rows on purpose: unlike domains and groups, they are hard-wired into authorization policy, so adding one is a code change by definition.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ColumbusUserTests(PostgresFixture fixture)
{
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
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter ColumbusUserTests`
Expected: FAIL — `ColumbusUser` does not exist.

- [ ] **Step 3: Write the entity and enums**

```csharp
namespace MsRelationship.Api.Data.Entities;

public enum UserRole { Standard, Moderator, Admin, SuperAdmin }

public enum UserStatus { Active, Archived }

public class ColumbusUser
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid? DepartmentId { get; set; }
    public string[] Skills { get; set; } = [];
    public UserRole Role { get; set; } = UserRole.Standard;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? LastSurveyAt { get; set; }
}
```

Put `UserRole` and `UserStatus` in their own files to match the one-type-per-file convention the rest of `Entities/` uses.

- [ ] **Step 4: Map it**

In `OnModelCreating`:

```csharp
b.Entity<ColumbusUser>(e =>
{
    e.ToTable("columbus_users");
    e.HasIndex(x => x.Email).IsUnique();
    e.HasIndex(x => x.EntraObjectId).IsUnique().HasFilter("entra_object_id IS NOT NULL");
    e.Property(x => x.Role).HasConversion<string>();
    e.Property(x => x.Status).HasConversion<string>();
});
```

Add `public DbSet<ColumbusUser> ColumbusUsers => Set<ColumbusUser>();`.

- [ ] **Step 5: Create the migration**

```bash
cd backend && dotnet ef migrations add ColumbusUsers --project src/MsRelationship.Api
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter ColumbusUserTests`
Expected: PASS, both tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: columbus users with role and archive status"
```

---

### Task 5: Microsoft profiles and the canonical identity key (FR-18)

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsProfile.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/MsProfileDomain.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/IdentityKeyTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 3).
- Produces: `MsProfile { Guid Id; string Name; string Title; string? Email; string Organization; Guid? GroupId; Guid? SourceId; string Notes; bool IsTentative; Guid? MergedIntoId; string IdentityKey; DateTimeOffset UpdatedAt; }`. `IdentityKey` is database-generated and read-only in C#. `MsProfileDomain { Guid MsProfileId; Guid DomainId; }`.

`Organization` is new — it does not exist in the mockup — and defaults to `'Microsoft'`, which is correct for all 23 seeded profiles and gives the name+org fallback something to key on.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class IdentityKeyTests(PostgresFixture fixture)
{
    private static MsProfile Profile(string name, string? email = null, string org = "Microsoft") =>
        new() { Id = Guid.NewGuid(), Name = name, Email = email, Organization = org };

    [Fact]
    public async Task Email_wins_and_is_normalised()
    {
        await using var db = fixture.NewContext();
        var p = Profile("Nina Due", "  Nina.Due@Microsoft.com ");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        await using var read = fixture.NewContext();
        Assert.Equal("nina.due@microsoft.com", (await read.MsProfiles.SingleAsync(x => x.Id == p.Id)).IdentityKey);
    }

    [Fact]
    public async Task Falls_back_to_name_and_organization()
    {
        await using var db = fixture.NewContext();
        var p = Profile("Claus Iversen", null, "Microsoft");
        db.MsProfiles.Add(p);
        await db.SaveChangesAsync();

        await using var read = fixture.NewContext();
        Assert.Equal("claus iversen|microsoft", (await read.MsProfiles.SingleAsync(x => x.Id == p.Id)).IdentityKey);
    }

    [Fact]
    public async Task Live_duplicates_are_rejected()
    {
        await using var db = fixture.NewContext();
        db.MsProfiles.Add(Profile("Kris Rozanka"));
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.MsProfiles.Add(Profile("kris rozanka"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_tombstoned_duplicate_does_not_block_the_survivor()
    {
        await using var db = fixture.NewContext();
        var survivor = Profile("Mauro Dalvit");
        db.MsProfiles.Add(survivor);
        await db.SaveChangesAsync();

        // A merged-away record keeps the same identity key and must not trip the index.
        await using var second = fixture.NewContext();
        second.MsProfiles.Add(new MsProfile
        {
            Id = Guid.NewGuid(), Name = "Mauro Dalvit", Organization = "Microsoft", MergedIntoId = survivor.Id
        });

        await second.SaveChangesAsync();
        Assert.Equal(2, await second.MsProfiles.CountAsync(x => x.Name == "Mauro Dalvit"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter IdentityKeyTests`
Expected: FAIL — `MsProfile` does not exist.

- [ ] **Step 3: Write the entities**

```csharp
namespace MsRelationship.Api.Data.Entities;

public class MsProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Email { get; set; }
    public string Organization { get; set; } = "Microsoft";
    public Guid? GroupId { get; set; }
    public Guid? SourceId { get; set; }
    public string Notes { get; set; } = "";
    public bool IsTentative { get; set; }
    public Guid? MergedIntoId { get; set; }
    public string IdentityKey { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
namespace MsRelationship.Api.Data.Entities;

public class MsProfileDomain
{
    public Guid MsProfileId { get; set; }
    public Guid DomainId { get; set; }
}
```

- [ ] **Step 4: Map them, with the generated column**

```csharp
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
```

Add `public DbSet<MsProfile> MsProfiles => Set<MsProfile>();` and `public DbSet<MsProfileDomain> MsProfileDomains => Set<MsProfileDomain>();`.

- [ ] **Step 5: Create the migration and enable `pg_trgm`**

```bash
cd backend && dotnet ef migrations add MsProfiles --project src/MsRelationship.Api
```

Then hand-edit the generated migration's `Up` to begin with the extension Task 8's fuzzy matching needs:

```csharp
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter IdentityKeyTests`
Expected: PASS, all four tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: microsoft profiles with canonical identity key (FR-18)"
```

---

### Task 6: Relations (FR-01, FR-02)

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/Relation.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/RelationTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser` (Task 4), `MsProfile` (Task 5).
- Produces: `Relation { Guid Id; Guid ColumbusUserId; Guid MsProfileId; int Score; string Note; DateTimeOffset UpdatedAt; }`, unique on `(ColumbusUserId, MsProfileId)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(4)]
    [InlineData(-4)]
    public async Task Scores_outside_the_scale_are_rejected(int score)
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        db.Relations.Add(new Relation
        {
            Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = score
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_microsoft_person_takes_many_columbus_relations()
    {
        await using var db = fixture.NewContext();
        var (userA, profile) = await Seed.PairAsync(db);
        var userB = await Seed.UserAsync(db);

        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = userA.Id, MsProfileId = profile.Id, Score = 3 });
        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = userB.Id, MsProfileId = profile.Id, Score = 1 });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Relations.CountAsync(r => r.MsProfileId == profile.Id));
    }

    [Fact]
    public async Task The_same_pair_cannot_be_scored_twice()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        db.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = 2 });
        await db.SaveChangesAsync();

        await using var second = fixture.NewContext();
        second.Relations.Add(new Relation { Id = Guid.NewGuid(), ColumbusUserId = user.Id, MsProfileId = profile.Id, Score = 3 });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Write the shared test seed helper**

`backend/tests/MsRelationship.Api.Tests/Seed.cs`:

```csharp
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

public static class Seed
{
    public static async Task<ColumbusUser> UserAsync(AppDbContext db)
    {
        var user = new ColumbusUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@columbusglobal.com",
            Name = "Test User"
        };
        db.ColumbusUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<MsProfile> ProfileAsync(AppDbContext db, string? name = null)
    {
        var profile = new MsProfile
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"MS Person {Guid.NewGuid():N}",
            Organization = "Microsoft"
        };
        db.MsProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public static async Task<(ColumbusUser, MsProfile)> PairAsync(AppDbContext db) =>
        (await UserAsync(db), await ProfileAsync(db));
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd backend && dotnet test --filter RelationTests`
Expected: FAIL — `Relation` does not exist.

- [ ] **Step 4: Write the entity and mapping**

```csharp
namespace MsRelationship.Api.Data.Entities;

public class Relation
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public int Score { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
b.Entity<Relation>(e =>
{
    e.ToTable("relations", t =>
        t.HasCheckConstraint("ck_relations_score", "score BETWEEN -3 AND 3"));
    e.HasIndex(x => new { x.ColumbusUserId, x.MsProfileId }).IsUnique();
    e.HasOne<ColumbusUser>().WithMany().HasForeignKey(x => x.ColumbusUserId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<MsProfile>().WithMany().HasForeignKey(x => x.MsProfileId).OnDelete(DeleteBehavior.Restrict);
});
```

`DeleteBehavior.Restrict` is deliberate: it makes the "archive, never delete" rule a database guarantee rather than a convention someone can forget.

- [ ] **Step 5: Create the migration**

```bash
cd backend && dotnet ef migrations add Relations --project src/MsRelationship.Api
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter RelationTests`
Expected: PASS, all four cases.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: relations with score constraint and unique pairing (FR-01, FR-02)"
```

---

### Task 7: Append-only history and `RelationWriter` (FR-09)

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/RelationHistory.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/RelationChangeType.cs`
- Create: `backend/src/MsRelationship.Api/Features/Relations/RelationWriter.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/RelationWriterTests.cs`

**Interfaces:**
- Consumes: `Relation` (Task 6).
- Produces: `RelationWriter` with
  `Task<Relation> UpsertAsync(Guid columbusUserId, Guid msProfileId, int score, string note, Guid changedBy, Guid? submissionId = null)`
  and `Task RemoveAsync(Guid columbusUserId, Guid msProfileId, Guid changedBy, Guid? submissionId = null)`.
  Both append to `relation_history`. Tasks 9–12 call these and never touch `DbContext.Relations`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class RelationWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Creating_then_updating_leaves_two_history_rows()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        await writer.UpsertAsync(user.Id, profile.Id, 1, "first", user.Id);
        await writer.UpsertAsync(user.Id, profile.Id, 3, "second", user.Id);

        var history = await db.RelationHistory
            .Where(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id)
            .OrderBy(h => h.ChangedAt).ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(RelationChangeType.Created, history[0].ChangeType);
        Assert.Null(history[0].OldScore);
        Assert.Equal(1, history[0].NewScore);
        Assert.Equal(RelationChangeType.Updated, history[1].ChangeType);
        Assert.Equal(1, history[1].OldScore);
        Assert.Equal(3, history[1].NewScore);
    }

    [Fact]
    public async Task Removing_records_the_last_known_score()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var writer = new RelationWriter(db);

        await writer.UpsertAsync(user.Id, profile.Id, -2, "strained", user.Id);
        await writer.RemoveAsync(user.Id, profile.Id, user.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id));

        var last = await db.RelationHistory
            .Where(h => h.ColumbusUserId == user.Id && h.MsProfileId == profile.Id)
            .OrderByDescending(h => h.ChangedAt).FirstAsync();

        Assert.Equal(RelationChangeType.Removed, last.ChangeType);
        Assert.Equal(-2, last.OldScore);
        Assert.Null(last.NewScore);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter RelationWriterTests`
Expected: FAIL — `RelationWriter` does not exist.

- [ ] **Step 3: Write the history entity**

```csharp
namespace MsRelationship.Api.Data.Entities;

public enum RelationChangeType
{
    Created, Updated, Removed, MergeMoved, MergeDiscarded, UserArchived
}
```

```csharp
namespace MsRelationship.Api.Data.Entities;

public class RelationHistory
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public Guid MsProfileId { get; set; }
    public int? OldScore { get; set; }
    public int? NewScore { get; set; }
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public RelationChangeType ChangeType { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? SubmissionId { get; set; }
}
```

Map it, storing `ChangeType` as a string:

```csharp
b.Entity<RelationHistory>(e =>
{
    e.ToTable("relation_history");
    e.Property(x => x.ChangeType).HasConversion<string>();
    e.HasIndex(x => new { x.ColumbusUserId, x.MsProfileId });
    e.HasIndex(x => x.ChangedAt);
});
```

Add `public DbSet<RelationHistory> RelationHistory => Set<RelationHistory>();`.

- [ ] **Step 4: Write `RelationWriter`**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Relations;

public class RelationWriter(AppDbContext db)
{
    public async Task<Relation> UpsertAsync(
        Guid columbusUserId, Guid msProfileId, int score, string note, Guid changedBy, Guid? submissionId = null)
    {
        var existing = await db.Relations
            .SingleOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);

        var history = new RelationHistory
        {
            Id = Guid.NewGuid(),
            ColumbusUserId = columbusUserId,
            MsProfileId = msProfileId,
            OldScore = existing?.Score,
            OldNote = existing?.Note,
            NewScore = score,
            NewNote = note,
            ChangeType = existing is null ? RelationChangeType.Created : RelationChangeType.Updated,
            ChangedByUserId = changedBy,
            SubmissionId = submissionId
        };

        if (existing is null)
        {
            existing = new Relation
            {
                Id = Guid.NewGuid(), ColumbusUserId = columbusUserId, MsProfileId = msProfileId
            };
            db.Relations.Add(existing);
        }

        existing.Score = score;
        existing.Note = note;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        db.RelationHistory.Add(history);
        await db.SaveChangesAsync();
        return existing;
    }

    public async Task RemoveAsync(Guid columbusUserId, Guid msProfileId, Guid changedBy, Guid? submissionId = null)
    {
        var existing = await db.Relations
            .SingleOrDefaultAsync(r => r.ColumbusUserId == columbusUserId && r.MsProfileId == msProfileId);
        if (existing is null) return;

        db.RelationHistory.Add(new RelationHistory
        {
            Id = Guid.NewGuid(),
            ColumbusUserId = columbusUserId,
            MsProfileId = msProfileId,
            OldScore = existing.Score,
            OldNote = existing.Note,
            ChangeType = RelationChangeType.Removed,
            ChangedByUserId = changedBy,
            SubmissionId = submissionId
        });

        db.Relations.Remove(existing);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Register it and migrate**

In `Program.cs`: `builder.Services.AddScoped<RelationWriter>();`

```bash
cd backend && dotnet ef migrations add RelationHistory --project src/MsRelationship.Api
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter RelationWriterTests`
Expected: PASS, both tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: append-only relation history behind RelationWriter (FR-09)"
```

---

### Task 8: Duplicate matching before create (FR-19)

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfileMatcher.cs`
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfilesController.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/MatchTests.cs`

**Interfaces:**
- Consumes: `MsProfile` (Task 5).
- Produces: `MsProfileMatcher.FindAsync(string name, string? email, string organization)` returning `IReadOnlyList<ProfileMatch>` where `ProfileMatch { Guid Id; string Name; string? Email; double Confidence; string Reason; }`, ordered by descending confidence. `POST /api/ms-profiles/match` and `POST /api/ms-profiles`.

- [ ] **Step 1: Write the failing test**

```csharp
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MatchTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_exact_identity_key_is_the_top_match()
    {
        await using var db = fixture.NewContext();
        var existing = await Seed.ProfileAsync(db, "Nikhil Makkar");
        var matcher = new MsProfileMatcher(db);

        var matches = await matcher.FindAsync("Nikhil Makkar", null, "Microsoft");

        Assert.Equal(existing.Id, matches[0].Id);
        Assert.Equal(1.0, matches[0].Confidence);
        Assert.Equal("identity", matches[0].Reason);
    }

    [Fact]
    public async Task A_near_miss_on_spelling_still_surfaces()
    {
        await using var db = fixture.NewContext();
        var existing = await Seed.ProfileAsync(db, "Chandana Ramesh");
        var matcher = new MsProfileMatcher(db);

        var matches = await matcher.FindAsync("Chandanna Ramesh", null, "Microsoft");

        Assert.Contains(matches, m => m.Id == existing.Id && m.Reason == "similar-name");
    }

    [Fact]
    public async Task A_merged_away_record_is_never_offered()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db, "Bo Larsen");
        var tombstone = new MsProfile
        {
            Id = Guid.NewGuid(), Name = "Bo Larsen", Organization = "Microsoft", MergedIntoId = survivor.Id
        };
        db.MsProfiles.Add(tombstone);
        await db.SaveChangesAsync();

        var matches = await new MsProfileMatcher(db).FindAsync("Bo Larsen", null, "Microsoft");

        Assert.DoesNotContain(matches, m => m.Id == tombstone.Id);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter MatchTests`
Expected: FAIL — `MsProfileMatcher` does not exist.

- [ ] **Step 3: Write the matcher**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;

namespace MsRelationship.Api.Features.MsProfiles;

public record ProfileMatch(Guid Id, string Name, string? Email, double Confidence, string Reason);

public class MsProfileMatcher(AppDbContext db)
{
    private const double SimilarityFloor = 0.4;

    public static string KeyFor(string name, string? email, string organization) =>
        string.IsNullOrWhiteSpace(email)
            ? $"{name.Trim().ToLowerInvariant()}|{organization.Trim().ToLowerInvariant()}"
            : email.Trim().ToLowerInvariant();

    public async Task<IReadOnlyList<ProfileMatch>> FindAsync(string name, string? email, string organization)
    {
        var key = KeyFor(name, email, organization);
        var live = db.MsProfiles.Where(p => p.MergedIntoId == null);

        var exact = await live.Where(p => p.IdentityKey == key)
            .Select(p => new ProfileMatch(p.Id, p.Name, p.Email, 1.0, "identity"))
            .ToListAsync();

        var similar = await live
            .Where(p => p.IdentityKey != key)
            .Select(p => new { p.Id, p.Name, p.Email, Score = EF.Functions.TrigramsSimilarity(p.Name, name) })
            .Where(x => x.Score >= SimilarityFloor)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToListAsync();

        return [.. exact, .. similar.Select(x => new ProfileMatch(x.Id, x.Name, x.Email, x.Score, "similar-name"))];
    }
}
```

- [ ] **Step 4: Write the controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.MsProfiles;

public record MatchRequest(string Name, string? Email, string Organization = "Microsoft");
public record CreateMsProfileRequest(
    string Name, string Title, string? Email, string Organization,
    Guid? GroupId, Guid? SourceId, string Notes, Guid[] DomainIds);

[ApiController]
[Route("api/ms-profiles")]
public class MsProfilesController(AppDbContext db, MsProfileMatcher matcher) : ControllerBase
{
    [HttpPost("match")]
    public async Task<IReadOnlyList<ProfileMatch>> Match([FromBody] MatchRequest request) =>
        await matcher.FindAsync(request.Name, request.Email, request.Organization);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMsProfileRequest request)
    {
        var key = MsProfileMatcher.KeyFor(request.Name, request.Email, request.Organization);
        var clash = await db.MsProfiles
            .SingleOrDefaultAsync(p => p.MergedIntoId == null && p.IdentityKey == key);

        if (clash is not null)
            return Conflict(new { message = "A profile with this identity already exists.", existing = clash });

        var profile = new MsProfile
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Title = request.Title,
            Email = request.Email,
            Organization = request.Organization,
            GroupId = request.GroupId,
            SourceId = request.SourceId,
            Notes = request.Notes
        };
        db.MsProfiles.Add(profile);
        foreach (var domainId in request.DomainIds.Distinct())
            db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = profile.Id, DomainId = domainId });

        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Create), new { id = profile.Id }, profile);
    }
}
```

- [ ] **Step 5: Register the matcher**

In `Program.cs`: `builder.Services.AddScoped<MsProfileMatcher>();`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter MatchTests`
Expected: PASS, all three tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: match existing microsoft people before create (FR-19)"
```

---

### Task 9: Merge duplicates (FR-20)

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfileMerger.cs`
- Modify: `backend/src/MsRelationship.Api/Features/MsProfiles/MsProfilesController.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/MergeTests.cs`

**Interfaces:**
- Consumes: `RelationWriter` (Task 7), `MsProfile` (Task 5).
- Produces: `MsProfileMerger.MergeAsync(Guid survivorId, IReadOnlyList<Guid> mergeIds, Guid actorId)`. Endpoint `POST /api/ms-profiles/{survivorId}/merge`.

Collision rule, from spec §6: when the same Columbus user has a relation to both records, **the higher score survives**, and the discarded value is written to history as `MergeDiscarded` so nothing disappears silently.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MergeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Relations_move_to_the_survivor()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "knows them", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        Assert.True(await db.Relations.AnyAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id));
        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == duplicate.Id));
    }

    [Fact]
    public async Task A_collision_keeps_the_higher_score_and_records_the_loser()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 1, "weak", user.Id);
        await writer.UpsertAsync(user.Id, duplicate.Id, 3, "strong", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id);
        Assert.Equal(3, kept.Score);
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.ChangeType == RelationChangeType.MergeDiscarded && h.OldScore == 1));
    }

    [Fact]
    public async Task The_duplicate_is_tombstoned_not_deleted()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        var stored = await db.MsProfiles.SingleAsync(p => p.Id == duplicate.Id);
        Assert.Equal(survivor.Id, stored.MergedIntoId);
    }

    [Fact]
    public async Task Domains_are_unioned()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var domainA = new MsDomain { Id = Guid.NewGuid(), Name = $"A-{Guid.NewGuid():N}" };
        var domainB = new MsDomain { Id = Guid.NewGuid(), Name = $"B-{Guid.NewGuid():N}" };
        db.MsDomains.AddRange(domainA, domainB);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = survivor.Id, DomainId = domainA.Id });
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = domainB.Id });
        await db.SaveChangesAsync();

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        var domains = await db.MsProfileDomains.Where(x => x.MsProfileId == survivor.Id).ToListAsync();
        Assert.Equal(2, domains.Count);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter MergeTests`
Expected: FAIL — `MsProfileMerger` does not exist.

- [ ] **Step 3: Write the merger**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Features.MsProfiles;

public class MsProfileMerger(AppDbContext db, RelationWriter writer)
{
    public async Task MergeAsync(Guid survivorId, IReadOnlyList<Guid> mergeIds, Guid actorId)
    {
        if (mergeIds.Contains(survivorId))
            throw new InvalidOperationException("A profile cannot be merged into itself.");

        var survivor = await db.MsProfiles.SingleAsync(p => p.Id == survivorId && p.MergedIntoId == null);
        await using var tx = await db.Database.BeginTransactionAsync();

        foreach (var mergeId in mergeIds)
        {
            var loser = await db.MsProfiles.SingleAsync(p => p.Id == mergeId);

            var moving = await db.Relations.Where(r => r.MsProfileId == mergeId).ToListAsync();
            foreach (var relation in moving)
            {
                var clash = await db.Relations.SingleOrDefaultAsync(r =>
                    r.MsProfileId == survivorId && r.ColumbusUserId == relation.ColumbusUserId);

                if (clash is null)
                {
                    await writer.UpsertAsync(relation.ColumbusUserId, survivorId, relation.Score, relation.Note,
                        actorId);
                }
                else
                {
                    var winner = relation.Score > clash.Score ? relation : clash;
                    var discarded = ReferenceEquals(winner, relation) ? clash : relation;

                    db.RelationHistory.Add(new RelationHistory
                    {
                        Id = Guid.NewGuid(),
                        ColumbusUserId = discarded.ColumbusUserId,
                        MsProfileId = survivorId,
                        OldScore = discarded.Score,
                        OldNote = discarded.Note,
                        NewScore = winner.Score,
                        NewNote = winner.Note,
                        ChangeType = RelationChangeType.MergeDiscarded,
                        ChangedByUserId = actorId
                    });

                    clash.Score = winner.Score;
                    clash.Note = winner.Note;
                    clash.UpdatedAt = DateTimeOffset.UtcNow;
                }

                db.Relations.Remove(relation);
                await db.SaveChangesAsync();
            }

            await db.RelationHistory.Where(h => h.MsProfileId == mergeId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.MsProfileId, survivorId)
                    .SetProperty(h => h.ChangeType, RelationChangeType.MergeMoved));

            await db.SubmissionItems.Where(i => i.MsProfileId == mergeId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.MsProfileId, survivorId));

            var survivorDomains = await db.MsProfileDomains
                .Where(x => x.MsProfileId == survivorId).Select(x => x.DomainId).ToListAsync();
            var loserDomains = await db.MsProfileDomains
                .Where(x => x.MsProfileId == mergeId).ToListAsync();
            foreach (var link in loserDomains)
            {
                if (!survivorDomains.Contains(link.DomainId))
                    db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = survivorId, DomainId = link.DomainId });
                db.MsProfileDomains.Remove(link);
            }

            // Fill blanks on the survivor; never overwrite something already known.
            if (string.IsNullOrWhiteSpace(survivor.Email)) survivor.Email = loser.Email;
            if (string.IsNullOrWhiteSpace(survivor.Title)) survivor.Title = loser.Title;
            if (string.IsNullOrWhiteSpace(survivor.Notes)) survivor.Notes = loser.Notes;
            survivor.GroupId ??= loser.GroupId;
            survivor.SourceId ??= loser.SourceId;
            survivor.UpdatedAt = DateTimeOffset.UtcNow;

            loser.MergedIntoId = survivorId;
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
    }
}
```

`db.SubmissionItems` does not exist until Task 12. Write Task 12's entity first if the executor is following order strictly; otherwise comment that line out and restore it in Task 12 Step 5.

- [ ] **Step 4: Add the endpoint**

In `MsProfilesController`:

```csharp
public record MergeRequest(Guid[] MergeIds);

[HttpPost("{survivorId:guid}/merge")]
public async Task<IActionResult> Merge(Guid survivorId, [FromBody] MergeRequest request,
    [FromServices] MsProfileMerger merger, [FromServices] ICurrentUser currentUser)
{
    await merger.MergeAsync(survivorId, request.MergeIds, currentUser.Id);
    return NoContent();
}
```

`ICurrentUser` arrives in Task 13. Until then, resolve the actor from a route-supplied `actorId` and replace it in Task 13 Step 6.

- [ ] **Step 5: Register the merger**

In `Program.cs`: `builder.Services.AddScoped<MsProfileMerger>();`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter MergeTests`
Expected: PASS, all four tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: merge duplicate microsoft profiles, moving relations and history (FR-20)"
```

---

### Task 10: Archive a leaver instead of deleting them

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/ColumbusUsers/ColumbusUsersController.cs`
- Create: `backend/src/MsRelationship.Api/Features/ColumbusUsers/UserArchiver.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/ArchiveTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser` (Task 4), `Relation` (Task 6).
- Produces: `UserArchiver.ArchiveAsync(Guid userId, Guid actorId)`. Endpoint `POST /api/columbus-users/{id}/archive`.

This replaces `deleteColumbusUser` (`index.html:2497`), which destroys the user, their relations and their submissions — the exact loss the Graphic View KPI at `index.html:1904` warns about.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.ColumbusUsers;
using MsRelationship.Api.Features.Relations;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class ArchiveTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Archiving_keeps_the_relations()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, 3, "trusted", user.Id);
        var admin = await Seed.UserAsync(db);

        await new UserArchiver(db).ArchiveAsync(user.Id, admin.Id);

        var stored = await db.ColumbusUsers.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Archived, stored.Status);
        Assert.NotNull(stored.ArchivedAt);
        Assert.True(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
    }

    [Fact]
    public async Task Archiving_writes_history_for_each_relation()
    {
        await using var db = fixture.NewContext();
        var user = await Seed.UserAsync(db);
        var first = await Seed.ProfileAsync(db);
        var second = await Seed.ProfileAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, first.Id, 1, "", user.Id);
        await writer.UpsertAsync(user.Id, second.Id, 2, "", user.Id);
        var admin = await Seed.UserAsync(db);

        await new UserArchiver(db).ArchiveAsync(user.Id, admin.Id);

        var archived = await db.RelationHistory
            .CountAsync(h => h.ColumbusUserId == user.Id && h.ChangeType == RelationChangeType.UserArchived);
        Assert.Equal(2, archived);
    }

    [Fact]
    public async Task The_super_admin_cannot_be_archived()
    {
        await using var db = fixture.NewContext();
        var superAdmin = await Seed.UserAsync(db);
        superAdmin.Role = UserRole.SuperAdmin;
        await db.SaveChangesAsync();
        var admin = await Seed.UserAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UserArchiver(db).ArchiveAsync(superAdmin.Id, admin.Id));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter ArchiveTests`
Expected: FAIL — `UserArchiver` does not exist.

- [ ] **Step 3: Write the archiver**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

public class UserArchiver(AppDbContext db)
{
    public async Task ArchiveAsync(Guid userId, Guid actorId)
    {
        var user = await db.ColumbusUsers.SingleAsync(u => u.Id == userId);
        if (user.Role == UserRole.SuperAdmin)
            throw new InvalidOperationException("The Super Admin profile cannot be archived. Transfer the role first.");
        if (user.Status == UserStatus.Archived) return;

        var relations = await db.Relations.Where(r => r.ColumbusUserId == userId).ToListAsync();
        foreach (var relation in relations)
        {
            db.RelationHistory.Add(new RelationHistory
            {
                Id = Guid.NewGuid(),
                ColumbusUserId = userId,
                MsProfileId = relation.MsProfileId,
                OldScore = relation.Score,
                NewScore = relation.Score,
                OldNote = relation.Note,
                NewNote = relation.Note,
                ChangeType = RelationChangeType.UserArchived,
                ChangedByUserId = actorId
            });
        }

        user.Status = UserStatus.Archived;
        user.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Write the controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

[ApiController]
[Route("api/columbus-users")]
public class ColumbusUsersController(AppDbContext db, UserArchiver archiver) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ColumbusUser>> List([FromQuery] bool includeArchived = false) =>
        await db.ColumbusUsers
            .Where(u => includeArchived || u.Status == UserStatus.Active)
            .OrderBy(u => u.Name)
            .ToListAsync();

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, [FromServices] ICurrentUser currentUser)
    {
        await archiver.ArchiveAsync(id, currentUser.Id);
        return NoContent();
    }
}
```

`includeArchived` defaults to `false`, which is what makes archived leavers disappear from Columbus Profiles unless the toggle is on (spec §8).

- [ ] **Step 5: Register the archiver**

In `Program.cs`: `builder.Services.AddScoped<UserArchiver>();`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter ArchiveTests`
Expected: PASS, all three tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: archive leavers and keep their relationship history"
```

---

### Task 11: Super Admin transfer (R-01)

**Files:**
- Create: `backend/src/MsRelationship.Api/Features/ColumbusUsers/SuperAdminTransfer.cs`
- Modify: `backend/src/MsRelationship.Api/Features/ColumbusUsers/ColumbusUsersController.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/SuperAdminTransferTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser` (Task 4).
- Produces: `SuperAdminTransfer.TransferAsync(Guid fromUserId, Guid toUserId)`. Endpoint `POST /api/columbus-users/{id}/promote-super-admin`.

R-01 says the role "shall be transferable", and there is exactly one holder at a time.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.ColumbusUsers;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SuperAdminTransferTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Transfer_demotes_the_holder_to_admin()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var target = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        await new SuperAdminTransfer(db).TransferAsync(holder.Id, target.Id);

        Assert.Equal(UserRole.Admin, (await db.ColumbusUsers.SingleAsync(u => u.Id == holder.Id)).Role);
        Assert.Equal(UserRole.SuperAdmin, (await db.ColumbusUsers.SingleAsync(u => u.Id == target.Id)).Role);
    }

    [Fact]
    public async Task Only_the_current_holder_can_transfer()
    {
        await using var db = fixture.NewContext();
        var impostor = await Seed.UserAsync(db);
        impostor.Role = UserRole.Admin;
        var target = await Seed.UserAsync(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(impostor.Id, target.Id));
    }

    [Fact]
    public async Task An_archived_user_cannot_receive_the_role()
    {
        await using var db = fixture.NewContext();
        var holder = await Seed.UserAsync(db);
        holder.Role = UserRole.SuperAdmin;
        var target = await Seed.UserAsync(db);
        target.Status = UserStatus.Archived;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SuperAdminTransfer(db).TransferAsync(holder.Id, target.Id));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter SuperAdminTransferTests`
Expected: FAIL — `SuperAdminTransfer` does not exist.

- [ ] **Step 3: Write the transfer service**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

public class SuperAdminTransfer(AppDbContext db)
{
    public async Task TransferAsync(Guid fromUserId, Guid toUserId)
    {
        if (fromUserId == toUserId) return;

        var holder = await db.ColumbusUsers.SingleAsync(u => u.Id == fromUserId);
        if (holder.Role != UserRole.SuperAdmin)
            throw new InvalidOperationException("Only the current Super Admin can transfer the role.");

        var target = await db.ColumbusUsers.SingleAsync(u => u.Id == toUserId);
        if (target.Status != UserStatus.Active)
            throw new InvalidOperationException("An archived user cannot become Super Admin.");

        await using var tx = await db.Database.BeginTransactionAsync();
        holder.Role = UserRole.Admin;
        target.Role = UserRole.SuperAdmin;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
```

- [ ] **Step 4: Add the endpoint**

In `ColumbusUsersController`:

```csharp
[HttpPost("{id:guid}/promote-super-admin")]
public async Task<IActionResult> PromoteSuperAdmin(Guid id,
    [FromServices] SuperAdminTransfer transfer, [FromServices] ICurrentUser currentUser)
{
    await transfer.TransferAsync(currentUser.Id, id);
    return NoContent();
}
```

- [ ] **Step 5: Register it**

In `Program.cs`: `builder.Services.AddScoped<SuperAdminTransfer>();`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter SuperAdminTransferTests`
Expected: PASS, all three tests.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: transferable super admin role (R-01)"
```

---

### Task 12: Submissions and approval (FR-07)

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Entities/Submission.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/SubmissionItem.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/SubmissionStatus.cs`
- Create: `backend/src/MsRelationship.Api/Data/Entities/SubmissionAction.cs`
- Create: `backend/src/MsRelationship.Api/Features/Submissions/SubmissionService.cs`
- Create: `backend/src/MsRelationship.Api/Features/Submissions/SubmissionsController.cs`
- Modify: `backend/src/MsRelationship.Api/Data/AppDbContext.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/SubmissionTests.cs`

**Interfaces:**
- Consumes: `RelationWriter` (Task 7).
- Produces: `Submission { Guid Id; Guid ColumbusUserId; DateTimeOffset SubmittedAt; SubmissionStatus Status; Guid? DecidedByUserId; DateTimeOffset? DecidedAt; }`, `SubmissionItem { Guid Id; Guid SubmissionId; Guid MsProfileId; SubmissionAction Action; int? NewScore; string? NewNote; int? PrevScore; string? PrevNote; }`, `SubmissionStatus { Pending, Approved, Rejected }`, `SubmissionAction { Upsert, Remove }`. `SubmissionService.SubmitAsync`, `.ApproveAsync`, `.RejectAsync`.

The approval path is the reason `RelationWriter` takes a `submissionId`: an approved change must be traceable to the submission that carried it.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SubmissionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Submitting_does_not_touch_approved_relations()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));

        await service.SubmitAsync(user.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 3, "new")]);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
        Assert.True(await db.Submissions.AnyAsync(s => s.Status == SubmissionStatus.Pending));
    }

    [Fact]
    public async Task Approving_writes_the_relation_and_links_the_history()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await service.ApproveAsync(submission.Id, admin.Id);

        var relation = await db.Relations.SingleAsync(r => r.ColumbusUserId == user.Id);
        Assert.Equal(2, relation.Score);
        Assert.True(await db.RelationHistory.AnyAsync(h => h.SubmissionId == submission.Id));
        Assert.Equal(SubmissionStatus.Approved,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Rejecting_leaves_the_approved_data_alone()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, 1, "as approved", user.Id);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, -3, "wrong")]);

        await service.RejectAsync(submission.Id, admin.Id);

        Assert.Equal(1, (await db.Relations.SingleAsync(r => r.ColumbusUserId == user.Id)).Score);
        Assert.Equal(SubmissionStatus.Rejected,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd backend && dotnet test --filter SubmissionTests`
Expected: FAIL — `SubmissionService` does not exist.

- [ ] **Step 3: Write the entities**

```csharp
namespace MsRelationship.Api.Data.Entities;

public enum SubmissionStatus { Pending, Approved, Rejected }
```

```csharp
namespace MsRelationship.Api.Data.Entities;

public enum SubmissionAction { Upsert, Remove }
```

```csharp
namespace MsRelationship.Api.Data.Entities;

public class Submission
{
    public Guid Id { get; set; }
    public Guid ColumbusUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
```

```csharp
namespace MsRelationship.Api.Data.Entities;

public class SubmissionItem
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid MsProfileId { get; set; }
    public SubmissionAction Action { get; set; }
    public int? NewScore { get; set; }
    public string? NewNote { get; set; }
    public int? PrevScore { get; set; }
    public string? PrevNote { get; set; }
}
```

Map both, storing the enums as strings, and add `DbSet<Submission> Submissions` and `DbSet<SubmissionItem> SubmissionItems`.

- [ ] **Step 4: Write the service**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;

namespace MsRelationship.Api.Features.Submissions;

public record SubmissionDraft(Guid MsProfileId, SubmissionAction Action, int? Score, string? Note);

public class SubmissionService(AppDbContext db, RelationWriter writer)
{
    public async Task<Submission> SubmitAsync(Guid columbusUserId, IReadOnlyList<SubmissionDraft> drafts)
    {
        var submission = new Submission { Id = Guid.NewGuid(), ColumbusUserId = columbusUserId };
        db.Submissions.Add(submission);

        foreach (var draft in drafts)
        {
            var current = await db.Relations.SingleOrDefaultAsync(r =>
                r.ColumbusUserId == columbusUserId && r.MsProfileId == draft.MsProfileId);

            db.SubmissionItems.Add(new SubmissionItem
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                MsProfileId = draft.MsProfileId,
                Action = draft.Action,
                NewScore = draft.Score,
                NewNote = draft.Note,
                PrevScore = current?.Score,
                PrevNote = current?.Note
            });
        }

        await db.SaveChangesAsync();
        return submission;
    }

    public async Task ApproveAsync(Guid submissionId, Guid adminId)
    {
        var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
        if (submission.Status != SubmissionStatus.Pending)
            throw new InvalidOperationException("This submission has already been decided.");

        var items = await db.SubmissionItems.Where(i => i.SubmissionId == submissionId).ToListAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var item in items)
        {
            if (item.Action == SubmissionAction.Remove)
                await writer.RemoveAsync(submission.ColumbusUserId, item.MsProfileId, adminId, submissionId);
            else
                await writer.UpsertAsync(submission.ColumbusUserId, item.MsProfileId,
                    item.NewScore ?? 0, item.NewNote ?? "", adminId, submissionId);
        }

        submission.Status = SubmissionStatus.Approved;
        submission.DecidedByUserId = adminId;
        submission.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RejectAsync(Guid submissionId, Guid adminId)
    {
        var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
        if (submission.Status != SubmissionStatus.Pending)
            throw new InvalidOperationException("This submission has already been decided.");

        submission.Status = SubmissionStatus.Rejected;
        submission.DecidedByUserId = adminId;
        submission.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
```

Rejected submissions are kept, not deleted — the mockup discards them (`index.html:2818`), but keeping them costs nothing and preserves the record of what was proposed.

- [ ] **Step 5: Restore the merge reference**

If Task 9 Step 3 commented out the `db.SubmissionItems` update, uncomment it now and re-run `dotnet test --filter MergeTests` to confirm it still passes.

- [ ] **Step 6: Write the controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.Submissions;

public record SubmitRequest(SubmissionDraft[] Items);

[ApiController]
[Route("api/submissions")]
public class SubmissionsController(AppDbContext db, SubmissionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Submission>> Pending() =>
        await db.Submissions.Where(s => s.Status == SubmissionStatus.Pending)
            .OrderBy(s => s.SubmittedAt).ToListAsync();

    [HttpPost]
    public async Task<Submission> Submit([FromBody] SubmitRequest request,
        [FromServices] ICurrentUser currentUser) =>
        await service.SubmitAsync(currentUser.Id, request.Items);

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromServices] ICurrentUser currentUser)
    {
        await service.ApproveAsync(id, currentUser.Id);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromServices] ICurrentUser currentUser)
    {
        await service.RejectAsync(id, currentUser.Id);
        return NoContent();
    }
}
```

- [ ] **Step 7: Migrate, register, run the tests**

```bash
cd backend
dotnet ef migrations add Submissions --project src/MsRelationship.Api
dotnet test --filter "SubmissionTests|MergeTests"
```

Expected: PASS, all seven tests. Register `SubmissionService` in `Program.cs` first.

- [ ] **Step 8: Commit**

```bash
git add backend
git commit -m "feat: submissions with approval writing through relation history (FR-07)"
```

---

### Task 13: Entra ID authentication and the closed membership list (NFR-02, FR-10, D-4)

**Files:**
- Create: `backend/src/MsRelationship.Api/Auth/ICurrentUser.cs`
- Create: `backend/src/MsRelationship.Api/Auth/CurrentUser.cs`
- Create: `backend/src/MsRelationship.Api/Auth/MembershipMiddleware.cs`
- Modify: `backend/src/MsRelationship.Api/Program.cs`
- Test: `backend/tests/MsRelationship.Api.Tests/MembershipTests.cs`

**Interfaces:**
- Consumes: `ColumbusUser` (Task 4).
- Produces: `ICurrentUser { Guid Id; UserRole Role; string Email; }`, registered scoped. Policies `CanEdit` (Admin, SuperAdmin) and `CanAdminister` (Admin, SuperAdmin). Tasks 9–12 already reference `ICurrentUser`.

Two independent gates, both server-side: the email domain, then the closed list.

- [ ] **Step 1: Add the packages**

```bash
cd backend
dotnet add src/MsRelationship.Api package Microsoft.Identity.Web --version 3.*
```

- [ ] **Step 2: Write the failing test**

```csharp
using MsRelationship.Api.Auth;
using Xunit;

namespace MsRelationship.Api.Tests;

public class MembershipTests
{
    [Theory]
    [InlineData("someone@columbusglobal.com", true)]
    [InlineData("someone@COLUMBUSGLOBAL.COM", true)]
    [InlineData("someone@gmail.com", false)]
    [InlineData("", false)]
    public void Allowed_domains_are_matched_case_insensitively(string email, bool expected) =>
        Assert.Equal(expected, MembershipMiddleware.IsAllowedDomain(email, ["columbusglobal.com"], isDevelopment: false));

    [Fact]
    public void An_empty_allow_list_permits_anyone_in_development_only()
    {
        Assert.True(MembershipMiddleware.IsAllowedDomain("anyone@example.com", [], isDevelopment: true));
        Assert.False(MembershipMiddleware.IsAllowedDomain("anyone@example.com", [], isDevelopment: false));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd backend && dotnet test --filter MembershipTests`
Expected: FAIL — `MembershipMiddleware` does not exist.

- [ ] **Step 4: Write the middleware**

```csharp
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

public class MembershipMiddleware(RequestDelegate next, IWebHostEnvironment env, IConfiguration config)
{
    public static bool IsAllowedDomain(string email, IReadOnlyList<string> allowed, bool isDevelopment)
    {
        if (allowed.Count == 0) return isDevelopment;
        if (string.IsNullOrWhiteSpace(email)) return false;

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;

        var domain = email[(at + 1)..];
        return allowed.Any(a => string.Equals(a.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db, CurrentUser currentUser)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var email = context.User.FindFirstValue("preferred_username")
                    ?? context.User.FindFirstValue(ClaimTypes.Email)
                    ?? "";

        var allowed = (config["AzureAd:AllowedEmailDomains"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!IsAllowedDomain(email, allowed, env.IsDevelopment()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "This account is outside Columbus." });
            return;
        }

        var objectId = context.User.FindFirstValue("oid");
        var user = await db.ColumbusUsers.SingleOrDefaultAsync(u =>
            u.EntraObjectId == objectId || u.Email.ToLower() == email.ToLower());

        if (user is null || user.Status != UserStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "This account is not registered. An administrator must add you first."
            });
            return;
        }

        // First successful sign-in binds the Entra object id to the pre-created row.
        if (user.EntraObjectId is null && objectId is not null)
        {
            user.EntraObjectId = objectId;
            await db.SaveChangesAsync();
        }

        currentUser.Bind(user);

        // The role lives in columbus_users, not in the Entra token, so stamp it onto the
        // principal here. Without this, RequireRole in the policies below never matches.
        context.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Role, user.Role.ToString())]));

        await next(context);
    }
}
```

The `AddIdentity` line is what makes the policies in Step 6 work. A role held in the database and a policy asserted against token claims is a silent authorization hole — the policy would simply never grant, and every admin endpoint would 403 in a way that looks like a configuration problem.

- [ ] **Step 5: Write `ICurrentUser`**

```csharp
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

public interface ICurrentUser
{
    Guid Id { get; }
    UserRole Role { get; }
    string Email { get; }
}
```

```csharp
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Auth;

public class CurrentUser : ICurrentUser
{
    public Guid Id { get; private set; }
    public UserRole Role { get; private set; }
    public string Email { get; private set; } = "";

    public void Bind(ColumbusUser user)
    {
        Id = user.Id;
        Role = user.Role;
        Email = user.Email;
    }
}
```

- [ ] **Step 6: Wire it up in `Program.cs`**

```csharp
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanEdit", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)))
    .AddPolicy("CanAdminister", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)));
```

After `var app = builder.Build();`:

```csharp
app.UseAuthentication();
app.UseMiddleware<MembershipMiddleware>();
app.UseAuthorization();
```

Then replace the interim `actorId` parameters left by Tasks 9, 10, 11 and 12 with `ICurrentUser`, and apply `[Authorize(Policy = "CanEdit")]` to the merge, archive and approval endpoints. `/health` keeps `[AllowAnonymous]`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS — the full suite, including every earlier task's tests.

- [ ] **Step 8: Commit**

```bash
git add backend
git commit -m "feat: entra authentication with domain gate and closed membership list"
```

---

### Task 14: Seed the approved mockup data

Seeding from the mockup rather than by hand means the API serves exactly the dataset the approved screens were designed against, so any later fidelity comparison is like-for-like.

**Files:**
- Create: `backend/src/MsRelationship.Api/Data/Seeder.cs`
- Create: `backend/src/MsRelationship.Api/Data/seed.json`
- Test: `backend/tests/MsRelationship.Api.Tests/SeedTests.cs`

**Interfaces:**
- Consumes: every entity from Tasks 3–7.
- Produces: `Seeder.SeedAsync(AppDbContext db)`, idempotent — running it twice leaves the same row counts.

- [ ] **Step 1: Extract the mockup's data into `seed.json`**

The source arrays are `state.domains` (`index.html:787`), `state.msProfiles` (`index.html:802`), `state.columbusProfiles` (`index.html:828`), and `state.relations` (`index.html:846`). Extract them with node rather than by hand:

```bash
node -e "
const fs = require('fs');
const src = fs.readFileSync('index.html', 'utf8');
const body = src.split('<script>')[1].split('</script>')[0];
const sandbox = {};
new Function('window', body.replace(/document\.|window\./g, 'sandbox.'))(sandbox);
" 2>/dev/null || echo "Direct evaluation is unsafe here — copy the four literal arrays out by hand instead."
```

If that fails — it will, because the mockup's script touches `document` at load — copy the four array literals directly out of `index.html` into `seed.json` as JSON, converting the single-quoted JS strings to double-quoted JSON. Keep the mockup's ids (`d1`, `m1`, `c1`, `r1`) as a `legacyId` field so the mapping stays checkable.

- [ ] **Step 2: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SeedTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeding_loads_the_approved_dataset()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        Assert.Equal(23, await db.MsProfiles.CountAsync());
        Assert.Equal(9, await db.MsDomains.CountAsync());       // eight agreed domains plus Unmarked
        Assert.True(await db.MsDomains.AnyAsync(d => d.IsSystem));
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);
        var before = await db.MsProfiles.CountAsync();
        await Seeder.SeedAsync(db);

        Assert.Equal(before, await db.MsProfiles.CountAsync());
    }
}
```

Confirm the two counts against the mockup before running — `grep -c "id:'m" index.html` for profiles, and the length of `state.domains` for domains — and correct the expected numbers if they differ.

- [ ] **Step 3: Run it to verify it fails**

Run: `cd backend && dotnet test --filter SeedTests`
Expected: FAIL — `Seeder` does not exist.

- [ ] **Step 4: Write the seeder**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Data;

public record SeedDomain(string LegacyId, string Name, string Owner, string Desc, bool System = false);
public record SeedProfile(string LegacyId, string Name, string Title, string? Email,
    string Group, string Source, string[] DomainLegacyIds, string Notes, bool Tentative);
public record SeedFile(SeedDomain[] Domains, SeedProfile[] MsProfiles);

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.MsProfiles.AnyAsync()) return;

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var domainIds = new Dictionary<string, Guid>();
        foreach (var (domain, index) in seed.Domains.Select((d, i) => (d, i)))
        {
            var id = Guid.NewGuid();
            domainIds[domain.LegacyId] = id;
            db.MsDomains.Add(new MsDomain
            {
                Id = id, Name = domain.Name, Owner = domain.Owner,
                Description = domain.Desc, IsSystem = domain.System, SortOrder = index
            });
        }

        var groupIds = await UpsertTaxonomyAsync(db, seed.MsProfiles.Select(p => p.Group).Distinct());
        var sourceIds = await UpsertSourcesAsync(db, seed.MsProfiles.Select(p => p.Source).Distinct());

        foreach (var profile in seed.MsProfiles)
        {
            var id = Guid.NewGuid();
            db.MsProfiles.Add(new MsProfile
            {
                Id = id, Name = profile.Name, Title = profile.Title,
                Email = string.IsNullOrWhiteSpace(profile.Email) ? null : profile.Email,
                Organization = "Microsoft",
                GroupId = groupIds[profile.Group], SourceId = sourceIds[profile.Source],
                Notes = profile.Notes, IsTentative = profile.Tentative
            });
            foreach (var legacy in profile.DomainLegacyIds)
                db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = id, DomainId = domainIds[legacy] });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, Guid>> UpsertTaxonomyAsync(AppDbContext db, IEnumerable<string> names)
    {
        var map = new Dictionary<string, Guid>();
        foreach (var name in names)
        {
            var id = Guid.NewGuid();
            db.MsGroups.Add(new MsGroup { Id = id, Name = name });
            map[name] = id;
        }
        await db.SaveChangesAsync();
        return map;
    }

    private static async Task<Dictionary<string, Guid>> UpsertSourcesAsync(AppDbContext db, IEnumerable<string> names)
    {
        var map = new Dictionary<string, Guid>();
        foreach (var name in names)
        {
            var id = Guid.NewGuid();
            db.MsSources.Add(new MsSource { Id = id, Name = name });
            map[name] = id;
        }
        await db.SaveChangesAsync();
        return map;
    }
}
```

- [ ] **Step 5: Ship `seed.json` with the build**

In `MsRelationship.Api.csproj`:

```xml
<ItemGroup>
  <None Update="Data/seed.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet test`
Expected: PASS — the full suite.

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: seed the approved mockup dataset"
```

---

## Definition of done for this plan

- [ ] `docker compose up -d db mail` brings up a healthy stack.
- [ ] `cd backend && dotnet test` passes with every task's tests green.
- [ ] `curl http://localhost:5080/health` returns `ok`.
- [ ] `.env` is untracked; `.env.example` is committed.
- [ ] The requirements traced here — FR-01, FR-02, FR-05, FR-07, FR-09, FR-10, FR-18, FR-19, FR-20, NFR-01, NFR-02, R-01 — each have a passing test.

## What this plan deliberately leaves out

Frontend of any kind (Plans 2–5), survey mail dispatch and scheduling (Plan 4), Excel/CSV export (Plan 5), and FR-14 / FR-16 / FR-17, which D-3 puts outside v1.
