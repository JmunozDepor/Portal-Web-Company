# Modo desatendido SAP + catálogos locales - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Modulo.Rendiciones operate per-company without a live SAP connection, by making local `CostCenter`/`GlAccount` tables the only catalog source the day-to-day flow reads from, filled either by a manual SAP sync or by hand.

**Architecture:** New `SapCatalogSyncEnabled` toggle on `RendicionesSettings` (per company). Two new local catalog tables (`CostCenter`, `GlAccount`) in the plugin's own dual-engine DB. A new `ICatalogSyncProvider` abstraction with one implementation (`SapCatalogSyncProvider`) that upserts from the existing live SAP contracts into the local tables on demand. Existing SAP-live read paths (`UserCostCenterService.GetAvailableAsync`, `TiposGasto` account search) switch to reading the local tables. New admin page `Configuracion/Integracion` exposes the toggle, a manual "Sincronizar ahora" action, and manual CRUD when SAP sync is off.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (dual engine: Npgsql + SqlServer via separate migrations projects), xUnit + EF Core InMemory for unit tests.

## Global Constraints

- Plugin references only `PortalSaas.Abstractions`, never `PortalSaas.Core`/`PortalSaas.Host` — no changes needed in Core/Abstractions for this plan, all work is inside the plugin repo.
- DB naming convention: snake_case, plural table names, `id`/`company_id` columns (see `docs/01-CONVENCION-NOMBRES-BD.md` in Core repo) — matches existing `rendiciones_settings`/`rendiciones_reminder_log` mapping style.
- Every entity change needs a migration in **both** `Modulo.Rendiciones.Migrations.Postgres` and `Modulo.Rendiciones.Migrations.SqlServer` projects (dual-engine — see `docs/09-GUIA-DESARROLLO-PLUGINS.md` §6.1).
- `CompanyId` is always mandatory on every new entity/query — no Organization-level fallback (established convention in this plugin).
- Follow the existing listado+"Editar" admin pattern (`admin-table`, `admin-link-action`, `admin-fila-activa` CSS classes already in `wwwroot/css/rendiciones.css`) — do not invent new table markup.
- Stop any locally-running `PortalSaas.Host`/`dotnet` process before rebuilding the plugin (`PublicarComoPlugin` MSBuild target fails with MSB3027 file-lock otherwise) — ask the user to confirm the Host is stopped before any build step in this plan.
- Never add automatic/periodic sync in this plan — sync is manual-only ("Sincronizar ahora" button), per spec scope.

---

## File Structure

New files:
- `src/Modulo.Rendiciones/Models/CostCenter.cs`
- `src/Modulo.Rendiciones/Models/GlAccount.cs`
- `src/Modulo.Rendiciones/Servicios/ICatalogSyncProvider.cs`
- `src/Modulo.Rendiciones/Servicios/SapCatalogSyncProvider.cs`
- `src/Modulo.Rendiciones/Pages/Configuracion/Integracion/Index.cshtml`
- `src/Modulo.Rendiciones/Pages/Configuracion/Integracion/Index.cshtml.cs`
- `src/Modulo.Rendiciones.Tests/Servicios/SapCatalogSyncProviderTests.cs`
- Migration files under both `Modulo.Rendiciones.Migrations.Postgres/Migrations/` and `Modulo.Rendiciones.Migrations.SqlServer/Migrations/` (generated via `dotnet ef migrations add`, not hand-written).

Modified files:
- `src/Modulo.Rendiciones/Models/RendicionesSettings.cs` — add `SapCatalogSyncEnabled`.
- `src/Modulo.Rendiciones/Data/RendicionesDbContext.cs` — map new field + two new entities, add `DbSet<CostCenter>`, `DbSet<GlAccount>`.
- `src/Modulo.Rendiciones/Servicios/UserCostCenterService.cs` — `GetAvailableAsync` reads local `CostCenter` table instead of live SAP.
- `src/Modulo.Rendiciones/Pages/Configuracion/TiposGasto/Index.cshtml.cs` — `OnGetSearchAccountsAsync` reads local `GlAccount` table instead of live SAP.
- `src/Modulo.Rendiciones/ModuloRendiciones.cs` — register `ICatalogSyncProvider`/`SapCatalogSyncProvider` in DI, add `config-integracion` menu item (Order 9).
- `src/Modulo.Rendiciones.Tests/Servicios/UserCostCenterServiceTests.cs` — update/add test for the new local-read fallback behavior.

---

### Task 1: `SapCatalogSyncEnabled` field on `RendicionesSettings`

**Files:**
- Modify: `src/Modulo.Rendiciones/Models/RendicionesSettings.cs`
- Modify: `src/Modulo.Rendiciones/Data/RendicionesDbContext.cs`
- Create: migration `AddSapCatalogSyncEnabled` in both migrations projects

**Interfaces:**
- Produces: `RendicionesSettings.SapCatalogSyncEnabled` (bool, default `true`) — read/written by Task 6's page and Task 3's provider guard logic.

- [ ] **Step 1: Add the field to the model**

Edit `Models/RendicionesSettings.cs`, adding the new property (keep existing three untouched):

```csharp
public class RendicionesSettings
{
    public required Guid CompanyId { get; set; }
    public TimeOnly ReminderHour { get; set; } = new(8, 0);
    public bool ReminderEnabled { get; set; } = true;
    public bool SapCatalogSyncEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Map the column**

Edit `Data/RendicionesDbContext.cs`, inside the existing `modelBuilder.Entity<RendicionesSettings>(e => { ... })` block, add:

```csharp
    e.Property(x => x.SapCatalogSyncEnabled).HasColumnName("sap_catalog_sync_enabled").IsRequired();
```

- [ ] **Step 3: Generate the Postgres migration**

Ask the user to confirm the local `PortalSaas.Host` is stopped, then run (adjust startup project/connection args to match how existing migrations in this repo were generated — check `Modulo.Rendiciones.Migrations.Postgres/Migrations/20260812114913_AddRendicionesSettingsAndReminderLog.cs` for the exact prior `dotnet ef` invocation pattern used in this repo, e.g. a `-p`/`-s` combo or a helper script):

```bash
dotnet ef migrations add AddSapCatalogSyncEnabled --project src/Modulo.Rendiciones.Migrations.Postgres --startup-project src/Modulo.Rendiciones.Migrations.Postgres
```

Expected: new files `<timestamp>_AddSapCatalogSyncEnabled.cs`/`.Designer.cs` under `Modulo.Rendiciones.Migrations.Postgres/Migrations/`, and `RendicionesDbContextModelSnapshot.cs` updated to include `sap_catalog_sync_enabled`.

- [ ] **Step 4: Generate the SqlServer migration**

```bash
dotnet ef migrations add AddSapCatalogSyncEnabled --project src/Modulo.Rendiciones.Migrations.SqlServer --startup-project src/Modulo.Rendiciones.Migrations.SqlServer
```

Expected: same as Step 3 but under `Modulo.Rendiciones.Migrations.SqlServer/Migrations/`.

- [ ] **Step 5: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Rendiciones/Models/RendicionesSettings.cs src/Modulo.Rendiciones/Data/RendicionesDbContext.cs src/Modulo.Rendiciones.Migrations.Postgres/Migrations src/Modulo.Rendiciones.Migrations.SqlServer/Migrations
git commit -m "feat: agrega SapCatalogSyncEnabled a RendicionesSettings"
```

---

### Task 2: Local `CostCenter`/`GlAccount` tables

**Files:**
- Create: `src/Modulo.Rendiciones/Models/CostCenter.cs`
- Create: `src/Modulo.Rendiciones/Models/GlAccount.cs`
- Modify: `src/Modulo.Rendiciones/Data/RendicionesDbContext.cs`
- Create: migration `AddCostCenterAndGlAccount` in both migrations projects

**Interfaces:**
- Produces: `CostCenter { Id (long), CompanyId (Guid), Code (string), Name (string), IsActive (bool), Source (CatalogEntrySource), UpdatedAt (DateTimeOffset) }`, same shape for `GlAccount`. `CatalogEntrySource` enum `{ Sap, Manual }`. `RendicionesDbContext.CostCenters` / `.GlAccounts` (`DbSet<T>`) — consumed by Task 3 (sync provider), Task 4 (`UserCostCenterService`), Task 5 (`TiposGasto`), Task 6 (admin page).

- [ ] **Step 1: Create the enum + models**

Create `Models/CatalogEntrySource.cs`:

```csharp
namespace Modulo.Rendiciones.Models;

public enum CatalogEntrySource
{
    Sap = 0,
    Manual = 1,
}
```

Create `Models/CostCenter.cs`:

```csharp
namespace Modulo.Rendiciones.Models;

public class CostCenter
{
    public long Id { get; set; }
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public CatalogEntrySource Source { get; set; } = CatalogEntrySource.Manual;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Create `Models/GlAccount.cs` (identical shape, separate class — do not share a base class, YAGNI until a third catalog type appears):

```csharp
namespace Modulo.Rendiciones.Models;

public class GlAccount
{
    public long Id { get; set; }
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public CatalogEntrySource Source { get; set; } = CatalogEntrySource.Manual;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Map both entities in `RendicionesDbContext`**

Add `DbSet` properties next to the existing ones:

```csharp
public DbSet<CostCenter> CostCenters => Set<CostCenter>();
public DbSet<GlAccount> GlAccounts => Set<GlAccount>();
```

Add to `OnModelCreating`:

```csharp
modelBuilder.Entity<CostCenter>(e =>
{
    e.ToTable("rendiciones_cost_centers");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
    e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
    e.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(50);
    e.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
    e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
    e.Property(x => x.Source).HasColumnName("source").IsRequired().HasConversion<int>();
    e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_rendiciones_cost_centers_company_id_code");
});

modelBuilder.Entity<GlAccount>(e =>
{
    e.ToTable("rendiciones_gl_accounts");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
    e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
    e.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(50);
    e.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
    e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
    e.Property(x => x.Source).HasColumnName("source").IsRequired().HasConversion<int>();
    e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_rendiciones_gl_accounts_company_id_code");
});
```

- [ ] **Step 3: Generate both migrations**

```bash
dotnet ef migrations add AddCostCenterAndGlAccount --project src/Modulo.Rendiciones.Migrations.Postgres --startup-project src/Modulo.Rendiciones.Migrations.Postgres
dotnet ef migrations add AddCostCenterAndGlAccount --project src/Modulo.Rendiciones.Migrations.SqlServer --startup-project src/Modulo.Rendiciones.Migrations.SqlServer
```

Expected: new migration files in both projects creating `rendiciones_cost_centers` and `rendiciones_gl_accounts` tables with the unique index on `(company_id, code)`.

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Modulo.Rendiciones/Models/CatalogEntrySource.cs src/Modulo.Rendiciones/Models/CostCenter.cs src/Modulo.Rendiciones/Models/GlAccount.cs src/Modulo.Rendiciones/Data/RendicionesDbContext.cs src/Modulo.Rendiciones.Migrations.Postgres/Migrations src/Modulo.Rendiciones.Migrations.SqlServer/Migrations
git commit -m "feat: agrega tablas locales rendiciones_cost_centers y rendiciones_gl_accounts"
```

---

### Task 3: `ICatalogSyncProvider` + `SapCatalogSyncProvider`

**Files:**
- Create: `src/Modulo.Rendiciones/Servicios/ICatalogSyncProvider.cs`
- Create: `src/Modulo.Rendiciones/Servicios/SapCatalogSyncProvider.cs`
- Modify: `src/Modulo.Rendiciones/ModuloRendiciones.cs`
- Test: `src/Modulo.Rendiciones.Tests/Servicios/SapCatalogSyncProviderTests.cs`

**Interfaces:**
- Consumes: `RendicionesDbContext.CostCenters`/`.GlAccounts` (Task 2), `ICostCenterCatalogService.ListAsync(string? searchText, int? limit, CancellationToken)` returning `IReadOnlyList<CostCenterDto>` with `.Code`/`.Name` (PortalSaas.Abstractions, existing), `IGeneralLedgerAccountCatalogService.ListAsync(string? searchText, int? limit, CancellationToken)` returning `IReadOnlyList<GeneralLedgerAccountDto>` with `.AccountCode`/`.AccountName` (existing).
- Produces: `ICatalogSyncProvider.SyncCostCentersAsync(Guid companyId, CancellationToken ct)` / `.SyncGlAccountsAsync(...)` returning `CatalogSyncResult { Created, Updated, Deactivated, Warnings }` — consumed by Task 6's page.

- [ ] **Step 1: Write the failing test**

Create `src/Modulo.Rendiciones.Tests/Servicios/SapCatalogSyncProviderTests.cs`. Follow the same `CreateDb(string dbName)` InMemory helper pattern as `UserCostCenterServiceTests.cs`, and a fake `ICostCenterCatalogService`/`IGeneralLedgerAccountCatalogService`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace Modulo.Rendiciones.Tests.Servicios;

public class SapCatalogSyncProviderTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class FakeCostCenterCatalogService : ICostCenterCatalogService
    {
        private readonly IReadOnlyList<CostCenterDto> _items;
        public FakeCostCenterCatalogService(IReadOnlyList<CostCenterDto> items) => _items = items;

        public Task<IReadOnlyList<CostCenterDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(_items);

        public Task<IReadOnlyList<CostCenterDto>> ListByDimensionAsync(int dimCode, string? searchText = null, int? limit = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeGlAccountCatalogService : IGeneralLedgerAccountCatalogService
    {
        private readonly IReadOnlyList<GeneralLedgerAccountDto> _items;
        public FakeGlAccountCatalogService(IReadOnlyList<GeneralLedgerAccountDto> items) => _items = items;

        public Task<IReadOnlyList<GeneralLedgerAccountDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(_items);
    }

    [Fact]
    public async Task SyncCostCentersAsync_creates_new_and_deactivates_missing()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_creates_new_and_deactivates_missing));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-OLD", Name = "Ya no existe en SAP", IsActive = true, Source = CatalogEntrySource.Sap });
        await db.SaveChangesAsync();

        var sapItems = new List<CostCenterDto> { new() { Code = "CC-NEW", Name = "Centro nuevo" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Deactivated);

        var created = await db.CostCenters.FirstAsync(c => c.Code == "CC-NEW" && c.CompanyId == companyId);
        Assert.Equal(CatalogEntrySource.Sap, created.Source);

        var deactivated = await db.CostCenters.FirstAsync(c => c.Code == "CC-OLD" && c.CompanyId == companyId);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task SyncCostCentersAsync_updates_name_of_existing_row_and_leaves_manual_rows_from_other_companies_untouched()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_updates_name_of_existing_row_and_leaves_manual_rows_from_other_companies_untouched));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-1", Name = "Nombre viejo", IsActive = true, Source = CatalogEntrySource.Sap });
        db.CostCenters.Add(new CostCenter { CompanyId = otherCompanyId, Code = "CC-1", Name = "No debe tocarse", IsActive = true, Source = CatalogEntrySource.Manual });
        await db.SaveChangesAsync();

        var sapItems = new List<CostCenterDto> { new() { Code = "CC-1", Name = "Nombre nuevo" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var updated = await db.CostCenters.FirstAsync(c => c.Code == "CC-1" && c.CompanyId == companyId);
        Assert.Equal("Nombre nuevo", updated.Name);

        var untouched = await db.CostCenters.FirstAsync(c => c.CompanyId == otherCompanyId);
        Assert.Equal("No debe tocarse", untouched.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Modulo.Rendiciones.Tests --filter SapCatalogSyncProviderTests`
Expected: FAIL — `SapCatalogSyncProvider`/`ICatalogSyncProvider`/`CatalogSyncResult` do not exist yet (compile error).

- [ ] **Step 3: Create `ICatalogSyncProvider`**

Create `Servicios/ICatalogSyncProvider.cs`:

```csharp
namespace Modulo.Rendiciones.Servicios;

public interface ICatalogSyncProvider
{
    Task<CatalogSyncResult> SyncCostCentersAsync(Guid companyId, CancellationToken ct = default);
    Task<CatalogSyncResult> SyncGlAccountsAsync(Guid companyId, CancellationToken ct = default);
}

public sealed record CatalogSyncResult(int Created, int Updated, int Deactivated, IReadOnlyList<string> Warnings);
```

- [ ] **Step 4: Implement `SapCatalogSyncProvider`**

Create `Servicios/SapCatalogSyncProvider.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Única implementación de ICatalogSyncProvider hoy -- envuelve los catálogos SAP en
/// vivo y hace upsert en las tablas locales rendiciones_cost_centers/rendiciones_gl_accounts.
/// Filas ausentes en el resultado de SAP se desactivan (IsActive = false), nunca se borran,
/// porque pueden estar referenciadas históricamente por gastos ya guardados.
/// </summary>
public sealed class SapCatalogSyncProvider : ICatalogSyncProvider
{
    private readonly RendicionesDbContext _db;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly IGeneralLedgerAccountCatalogService _glAccounts;

    public SapCatalogSyncProvider(RendicionesDbContext db, ICostCenterCatalogService costCenters, IGeneralLedgerAccountCatalogService glAccounts)
    {
        _db = db;
        _costCenters = costCenters;
        _glAccounts = glAccounts;
    }

    public async Task<CatalogSyncResult> SyncCostCentersAsync(Guid companyId, CancellationToken ct = default)
    {
        var sapItems = await _costCenters.ListAsync(ct: ct);
        var existing = await _db.CostCenters.Where(c => c.CompanyId == companyId).ToListAsync(ct);
        return await UpsertAsync(existing, sapItems.Select(i => (i.Code, i.Name)).ToList(), companyId,
            (companyIdArg, code, name) => new CostCenter { CompanyId = companyIdArg, Code = code, Name = name, Source = CatalogEntrySource.Sap }, ct);
    }

    public async Task<CatalogSyncResult> SyncGlAccountsAsync(Guid companyId, CancellationToken ct = default)
    {
        var sapItems = await _glAccounts.ListAsync(ct: ct);
        var existing = await _db.GlAccounts.Where(g => g.CompanyId == companyId).ToListAsync(ct);
        return await UpsertAsync(existing, sapItems.Select(i => (i.AccountCode, i.AccountName)).ToList(), companyId,
            (companyIdArg, code, name) => new GlAccount { CompanyId = companyIdArg, Code = code, Name = name, Source = CatalogEntrySource.Sap }, ct);
    }

    private async Task<CatalogSyncResult> UpsertAsync<TEntity>(
        List<TEntity> existing,
        IReadOnlyList<(string Code, string Name)> sapItems,
        Guid companyId,
        Func<Guid, string, string, TEntity> create,
        CancellationToken ct)
        where TEntity : class
    {
        int created = 0, updated = 0, deactivated = 0;
        var sapCodes = new HashSet<string>(sapItems.Select(i => i.Code));

        foreach (var (code, name) in sapItems)
        {
            var row = existing.FirstOrDefault(e => GetCode(e) == code);
            if (row is null)
            {
                _db.Set<TEntity>().Add(create(companyId, code, name));
                created++;
            }
            else if (GetName(row) != name || !GetIsActive(row))
            {
                SetName(row, name);
                SetIsActive(row, true);
                SetUpdatedAt(row);
                updated++;
            }
        }

        foreach (var row in existing.Where(e => GetIsActive(e) && !sapCodes.Contains(GetCode(e))))
        {
            SetIsActive(row, false);
            SetUpdatedAt(row);
            deactivated++;
        }

        await _db.SaveChangesAsync(ct);
        return new CatalogSyncResult(created, updated, deactivated, Array.Empty<string>());
    }

    private static string GetCode(object e) => e switch { CostCenter c => c.Code, GlAccount g => g.Code, _ => throw new NotSupportedException() };
    private static string GetName(object e) => e switch { CostCenter c => c.Name, GlAccount g => g.Name, _ => throw new NotSupportedException() };
    private static bool GetIsActive(object e) => e switch { CostCenter c => c.IsActive, GlAccount g => g.IsActive, _ => throw new NotSupportedException() };
    private static void SetName(object e, string name) { if (e is CostCenter c) c.Name = name; else if (e is GlAccount g) g.Name = name; }
    private static void SetIsActive(object e, bool value) { if (e is CostCenter c) c.IsActive = value; else if (e is GlAccount g) g.IsActive = value; }
    private static void SetUpdatedAt(object e) { if (e is CostCenter c) c.UpdatedAt = DateTimeOffset.UtcNow; else if (e is GlAccount g) g.UpdatedAt = DateTimeOffset.UtcNow; }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Modulo.Rendiciones.Tests --filter SapCatalogSyncProviderTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Register in DI**

Edit `ModuloRendiciones.cs`, in `RegisterServices`, add next to the other `AddScoped` calls:

```csharp
services.AddScoped<ICatalogSyncProvider, SapCatalogSyncProvider>();
```

- [ ] **Step 7: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Rendiciones/Servicios/ICatalogSyncProvider.cs src/Modulo.Rendiciones/Servicios/SapCatalogSyncProvider.cs src/Modulo.Rendiciones/ModuloRendiciones.cs src/Modulo.Rendiciones.Tests/Servicios/SapCatalogSyncProviderTests.cs
git commit -m "feat: ICatalogSyncProvider + SapCatalogSyncProvider (sync manual de catalogos SAP a tablas locales)"
```

---

### Task 4: `UserCostCenterService.GetAvailableAsync` reads local catalog

**Files:**
- Modify: `src/Modulo.Rendiciones/Servicios/UserCostCenterService.cs`
- Test: `src/Modulo.Rendiciones.Tests/Servicios/UserCostCenterServiceTests.cs`

**Interfaces:**
- Consumes: `RendicionesDbContext.CostCenters` (Task 2).
- Produces: `IUserCostCenterService.GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct)` unchanged signature, but fallback source changes from live SAP to local `CostCenter` table.

- [ ] **Step 1: Write the failing test**

Add to `src/Modulo.Rendiciones.Tests/Servicios/UserCostCenterServiceTests.cs` (same file, same `CreateDb` helper already present):

```csharp
[Fact]
public async Task GetAvailableAsync_falls_back_to_local_active_cost_centers_when_user_has_no_assignments()
{
    var companyId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    await using var db = CreateDb(nameof(GetAvailableAsync_falls_back_to_local_active_cost_centers_when_user_has_no_assignments));
    db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-A", Name = "Activo", IsActive = true, Source = CatalogEntrySource.Sap });
    db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-B", Name = "Inactivo", IsActive = false, Source = CatalogEntrySource.Sap });
    db.CostCenters.Add(new CostCenter { CompanyId = Guid.NewGuid(), Code = "CC-C", Name = "Otra compañía", IsActive = true, Source = CatalogEntrySource.Sap });
    await db.SaveChangesAsync();

    var sut = new UserCostCenterService(db);
    var result = await sut.GetAvailableAsync(companyId, userId, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal("CC-A", result[0].Code);
}
```

Remove the constructor argument for the SAP catalog fake in any test that previously passed one to `UserCostCenterService` — check the existing tests in this file that instantiate `new UserCostCenterService(db, fake)` and update them to `new UserCostCenterService(db)` (the SAP dependency is removed in Step 2 below).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Modulo.Rendiciones.Tests --filter UserCostCenterServiceTests`
Expected: FAIL to compile — constructor signature mismatch (old tests still pass a fake `ICostCenterCatalogService`).

- [ ] **Step 3: Update `UserCostCenterService`**

Edit `Servicios/UserCostCenterService.cs`. Remove the `ICostCenterCatalogService _costCenters` field and its constructor parameter (no longer needed — confirm no other method in this class uses `_costCenters` before removing; if another method does, keep the field and only change `GetAvailableAsync`'s body). Replace the fallback body:

```csharp
public async Task<IReadOnlyList<CostCenterDto>> GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct = default)
{
    var assigned = await ListAssignedAsync(companyId, userId, ct);
    if (assigned.Count > 0)
    {
        return assigned
            .Select(a => new CostCenterDto { Code = a.CostCenterCode, Name = a.CostCenterName ?? a.CostCenterCode })
            .ToList();
    }

    var local = await _db.CostCenters
        .Where(c => c.CompanyId == companyId && c.IsActive)
        .OrderBy(c => c.Name)
        .ToListAsync(ct);
    return local.Select(c => new CostCenterDto { Code = c.Code, Name = c.Name }).ToList();
}
```

- [ ] **Step 4: Update DI registration if the constructor signature changed**

If `ICostCenterCatalogService` was removed from `UserCostCenterService`'s constructor in Step 3, no DI change is needed (DI resolves whatever the constructor asks for) — just confirm `ModuloRendiciones.cs` still builds since `UserCostCenterService` is still registered as `AddScoped<IUserCostCenterService, UserCostCenterService>()`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Modulo.Rendiciones.Tests --filter UserCostCenterServiceTests`
Expected: PASS (all tests in the file, including the new one).

- [ ] **Step 6: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors. If `Pages/Configuracion/CentrosCostoUsuario/Index.cshtml.cs` still injects `ICostCenterCatalogService` directly (it uses `LoadCatalogSafeAsync` against it for the admin picker, per the spec this page's own live-SAP picker is unrelated to `GetAvailableAsync` and is out of scope for this plan — leave it as-is), confirm it still compiles independently.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Rendiciones/Servicios/UserCostCenterService.cs src/Modulo.Rendiciones.Tests/Servicios/UserCostCenterServiceTests.cs
git commit -m "fix: GetAvailableAsync usa catalogo local de centros de costo en vez de SAP en vivo"
```

---

### Task 5: `TiposGasto` account search reads local catalog

**Files:**
- Modify: `src/Modulo.Rendiciones/Pages/Configuracion/TiposGasto/Index.cshtml.cs`

**Interfaces:**
- Consumes: `RendicionesDbContext.GlAccounts` (Task 2).
- Produces: `OnGetSearchAccountsAsync(string text, CancellationToken ct)` — same JSON shape `{ AccountCode, AccountName }` consumed by the existing client-side search widget, unchanged so the frontend needs no changes.

- [ ] **Step 1: Replace the live SAP call**

Edit `Pages/Configuracion/TiposGasto/Index.cshtml.cs`. Ensure the page model has access to `RendicionesDbContext` (inject it in the constructor if not already present — follow the same pattern as `Pages/Configuracion/Notificaciones/Index.cshtml.cs`, which injects `RendicionesDbContext` directly) and `ICurrentCompanyAccessor` (already present, used elsewhere in this page for `CompanyId`). Replace:

```csharp
public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
{
    var accounts = await _accounts.ListAsync(text, 30, ct);
    return new JsonResult(accounts.Select(a => new { a.AccountCode, a.AccountName }));
}
```

with:

```csharp
public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
{
    var query = _db.GlAccounts.Where(a => a.CompanyId == _currentCompany.CompanyId && a.IsActive);
    if (!string.IsNullOrWhiteSpace(text))
        query = query.Where(a => a.Code.Contains(text) || a.Name.Contains(text));

    var accounts = await query.OrderBy(a => a.Name).Take(30).ToListAsync(ct);
    return new JsonResult(accounts.Select(a => new { AccountCode = a.Code, AccountName = a.Name }));
}
```

If the constructor previously injected `IGeneralLedgerAccountCatalogService _accounts` only for this handler (confirm by checking whether `_accounts` is used anywhere else in the file), remove that field/parameter; otherwise leave it in place for its other use and only change this handler's body.

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors. `using Microsoft.EntityFrameworkCore;` must be present in the file for `.Where`/`.Take`/`.ToListAsync` — add it if missing.

- [ ] **Step 3: Manual verification**

Ask the user to confirm the local Host is stopped, rebuild, run the app, and in `Configuracion/Tipos de Gasto`, type into the account search box for a company with at least one row in `rendiciones_gl_accounts` (seed one manually via SQL if none exist yet, since Task 6's UI to add one isn't built until the next task) and confirm matching results appear without any SAP call in the logs.

- [ ] **Step 4: Commit**

```bash
git add src/Modulo.Rendiciones/Pages/Configuracion/TiposGasto/Index.cshtml.cs
git commit -m "fix: busqueda de cuenta contable en TiposGasto usa catalogo local en vez de SAP en vivo"
```

---

### Task 6: `Configuracion/Integracion` admin page

**Files:**
- Create: `src/Modulo.Rendiciones/Pages/Configuracion/Integracion/Index.cshtml.cs`
- Create: `src/Modulo.Rendiciones/Pages/Configuracion/Integracion/Index.cshtml`
- Modify: `src/Modulo.Rendiciones/ModuloRendiciones.cs`

**Interfaces:**
- Consumes: `RendicionesSettings.SapCatalogSyncEnabled` (Task 1), `RendicionesDbContext.CostCenters`/`.GlAccounts` (Task 2), `ICatalogSyncProvider.SyncCostCentersAsync`/`.SyncGlAccountsAsync` (Task 3).

- [ ] **Step 1: Create the page model**

Create `Pages/Configuracion/Integracion/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Integracion;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly RendicionesDbContext _db;
    private readonly ICatalogSyncProvider _sync;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(RendicionesDbContext db, ICatalogSyncProvider sync, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _sync = sync;
        _currentCompany = currentCompany;
    }

    public bool SapCatalogSyncEnabled { get; private set; }
    public DateTimeOffset? SettingsUpdatedAt { get; private set; }
    public IReadOnlyList<CostCenter> CostCenters { get; private set; } = Array.Empty<CostCenter>();
    public IReadOnlyList<GlAccount> GlAccounts { get; private set; } = Array.Empty<GlAccount>();

    [BindProperty]
    public CatalogEntryInput NewCostCenter { get; set; } = new();

    [BindProperty]
    public CatalogEntryInput NewGlAccount { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(bool enabled, CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { CompanyId = _currentCompany.CompanyId };
            _db.RendicionesSettings.Add(settings);
        }
        settings.SapCatalogSyncEnabled = enabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = enabled ? "Sincronización con SAP activada." : "Sincronización con SAP desactivada. Los catálogos ahora se editan a mano.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSincronizarAsync(CancellationToken ct)
    {
        try
        {
            var costCenterResult = await _sync.SyncCostCentersAsync(_currentCompany.CompanyId, ct);
            var glAccountResult = await _sync.SyncGlAccountsAsync(_currentCompany.CompanyId, ct);
            SuccessMessage = $"Sincronizado. Centros de costo: {costCenterResult.Created} nuevos, {costCenterResult.Updated} actualizados, {costCenterResult.Deactivated} desactivados. " +
                              $"Cuentas contables: {glAccountResult.Created} nuevas, {glAccountResult.Updated} actualizadas, {glAccountResult.Deactivated} desactivadas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAgregarCentroCostoAsync(CancellationToken ct)
    {
        _db.CostCenters.Add(new CostCenter
        {
            CompanyId = _currentCompany.CompanyId,
            Code = NewCostCenter.Code,
            Name = NewCostCenter.Name,
            Source = CatalogEntrySource.Manual,
        });
        await _db.SaveChangesAsync(ct);
        SuccessMessage = "Centro de costo agregado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAgregarCuentaAsync(CancellationToken ct)
    {
        _db.GlAccounts.Add(new GlAccount
        {
            CompanyId = _currentCompany.CompanyId,
            Code = NewGlAccount.Code,
            Name = NewGlAccount.Name,
            Source = CatalogEntrySource.Manual,
        });
        await _db.SaveChangesAsync(ct);
        SuccessMessage = "Cuenta contable agregada.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarCentroCostoAsync(long id, CancellationToken ct)
    {
        var row = await _db.CostCenters.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
        if (row is not null)
        {
            row.IsActive = !row.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarCuentaAsync(long id, CancellationToken ct)
    {
        var row = await _db.GlAccounts.FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == _currentCompany.CompanyId, ct);
        if (row is not null)
        {
            row.IsActive = !row.IsActive;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == _currentCompany.CompanyId, ct);
        SapCatalogSyncEnabled = settings?.SapCatalogSyncEnabled ?? true;
        SettingsUpdatedAt = settings?.UpdatedAt;

        CostCenters = await _db.CostCenters.Where(c => c.CompanyId == _currentCompany.CompanyId).OrderBy(c => c.Name).ToListAsync(ct);
        GlAccounts = await _db.GlAccounts.Where(g => g.CompanyId == _currentCompany.CompanyId).OrderBy(g => g.Name).ToListAsync(ct);
    }

    public sealed class CatalogEntryInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Ingresá el código.")]
        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string Code { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Ingresá el nombre.")]
        [System.ComponentModel.DataAnnotations.StringLength(200)]
        public string Name { get; set; } = "";
    }
}
```

Note the `OnGetAsync` must call `LoadAsync` before every `Post*` handler returns `Page()` directly (none do here — all `Post*` handlers redirect via `RedirectToPage()`, so `OnGetAsync` always re-runs `LoadAsync` on the next request; this avoids the pitfall of returning `Page()` with stale/empty collections).

- [ ] **Step 2: Create the view**

Create `Pages/Configuracion/Integracion/Index.cshtml`:

```html
@page "/rendiciones/configuracion/integracion"
@model Modulo.Rendiciones.Pages.Configuracion.Integracion.IndexModel
@{
    ViewData["Title"] = "Integración";
}
@section Styles {
    <link rel="stylesheet" href="~/css/rendiciones.css" asp-append-version="true" />
}

<div class="card-ps admin-card">
    <div class="admin-card-header">
        <h2>Integración con SAP</h2>
    </div>

    @if (Model.SuccessMessage is not null)
    {
        <div class="admin-alert admin-alert-success">@Model.SuccessMessage</div>
    }
    @if (Model.ErrorMessage is not null)
    {
        <div class="admin-alert admin-alert-danger">@Model.ErrorMessage</div>
    }

    <p class="admin-muted">
        Cuando está activo, los centros de costo y cuentas contables se sincronizan desde SAP.
        Cuando está desactivado, se editan a mano desde esta página.
        @if (Model.SettingsUpdatedAt is { } updated)
        {
            <text>Último cambio: @updated.ToLocalTime().ToString("g").</text>
        }
    </p>

    <form method="post" asp-page-handler="Toggle">
        <input type="hidden" name="enabled" value="@(!Model.SapCatalogSyncEnabled)" />
        <button type="submit" class="btn-erp-primary--sm">
            @(Model.SapCatalogSyncEnabled ? "Desactivar sincronización SAP" : "Activar sincronización SAP")
        </button>
    </form>

    @if (Model.SapCatalogSyncEnabled)
    {
        <form method="post" asp-page-handler="Sincronizar" style="margin-top:1rem;">
            <button type="submit" class="btn-module-action">Sincronizar ahora</button>
        </form>
    }
</div>

<div class="card-ps admin-card" style="margin-top:1rem;">
    <div class="admin-card-header">
        <h3>Centros de costo</h3>
    </div>
    @if (!Model.SapCatalogSyncEnabled)
    {
        <form method="post" asp-page-handler="AgregarCentroCosto" class="admin-form-asignar">
            <div class="form-group">
                <label asp-for="NewCostCenter.Code">Código</label>
                <input asp-for="NewCostCenter.Code" class="form-control" />
            </div>
            <div class="form-group">
                <label asp-for="NewCostCenter.Name">Nombre</label>
                <input asp-for="NewCostCenter.Name" class="form-control" />
            </div>
            <button type="submit" class="btn-erp-primary--sm">Agregar</button>
        </form>
    }
    <table class="admin-table">
        <thead><tr><th>Código</th><th>Nombre</th><th>Origen</th><th>Estado</th><th class="admin-col-accion"></th></tr></thead>
        <tbody>
            @foreach (var c in Model.CostCenters)
            {
                <tr>
                    <td>@c.Code</td>
                    <td>@c.Name</td>
                    <td>@(c.Source == Modulo.Rendiciones.Models.CatalogEntrySource.Sap ? "SAP" : "Manual")</td>
                    <td>@(c.IsActive ? "Activo" : "Inactivo")</td>
                    <td class="admin-col-accion">
                        @if (!Model.SapCatalogSyncEnabled)
                        {
                            <form method="post" asp-page-handler="DesactivarCentroCosto" asp-route-id="@c.Id">
                                <button type="submit" class="admin-link-action">@(c.IsActive ? "Desactivar" : "Reactivar")</button>
                            </form>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>

<div class="card-ps admin-card" style="margin-top:1rem;">
    <div class="admin-card-header">
        <h3>Cuentas contables</h3>
    </div>
    @if (!Model.SapCatalogSyncEnabled)
    {
        <form method="post" asp-page-handler="AgregarCuenta" class="admin-form-asignar">
            <div class="form-group">
                <label asp-for="NewGlAccount.Code">Código</label>
                <input asp-for="NewGlAccount.Code" class="form-control" />
            </div>
            <div class="form-group">
                <label asp-for="NewGlAccount.Name">Nombre</label>
                <input asp-for="NewGlAccount.Name" class="form-control" />
            </div>
            <button type="submit" class="btn-erp-primary--sm">Agregar</button>
        </form>
    }
    <table class="admin-table">
        <thead><tr><th>Código</th><th>Nombre</th><th>Origen</th><th>Estado</th><th class="admin-col-accion"></th></tr></thead>
        <tbody>
            @foreach (var g in Model.GlAccounts)
            {
                <tr>
                    <td>@g.Code</td>
                    <td>@g.Name</td>
                    <td>@(g.Source == Modulo.Rendiciones.Models.CatalogEntrySource.Sap ? "SAP" : "Manual")</td>
                    <td>@(g.IsActive ? "Activo" : "Inactivo")</td>
                    <td class="admin-col-accion">
                        @if (!Model.SapCatalogSyncEnabled)
                        {
                            <form method="post" asp-page-handler="DesactivarCuenta" asp-route-id="@g.Id">
                                <button type="submit" class="admin-link-action">@(g.IsActive ? "Desactivar" : "Reactivar")</button>
                            </form>
                        }
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

Note: the `asp-route-id="@c.Id"` here is always a non-null `long` (never a nullable `Id` from a possibly-null object, unlike the `Detalle.cshtml` bug fixed earlier in this project) — safe to use `asp-route-id` directly.

- [ ] **Step 3: Register the menu item**

Edit `ModuloRendiciones.cs`, add after the `config-notificaciones` (Order 8) entry:

```csharp
yield return new MenuItemDefinition
{
    Code = "config-integracion",
    ParentCode = "grupo-administrador",
    Name = "Integración",
    PageRoute = "/rendiciones/configuracion/integracion",
    Order = 9,
};
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build src/Modulo.Rendiciones.sln -c Release
```

Expected: 0 errors.

- [ ] **Step 5: Manual verification**

Ask the user to confirm the local Host is stopped, rebuild, run the app, log in, and:
1. Open Configuración → Integración. Confirm the toggle shows "Desactivar sincronización SAP" by default (matches `SapCatalogSyncEnabled = true` default) and "Sincronizar ahora" is visible.
2. Click "Sincronizar ahora" against a company with a working SAP connection; confirm the summary message shows created/updated/deactivated counts and the tables below populate.
3. Click "Desactivar sincronización SAP"; confirm "Sincronizar ahora" disappears and the manual add-forms appear instead.
4. Add a manual cost center and a manual GL account; confirm they appear in the tables with "Manual" as origin.
5. Go to `Configuracion/Tipos de Gasto` and confirm the manually-added GL account now appears in the account search (validates Task 5's wiring end-to-end).
6. Click "Desactivar" on a manual row; confirm it flips to "Inactivo" and "Reactivar" appears.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Rendiciones/Pages/Configuracion/Integracion src/Modulo.Rendiciones/ModuloRendiciones.cs
git commit -m "feat: pagina Configuracion/Integracion (toggle SAP, sync manual, CRUD manual de catalogos)"
```

---

## Self-Review Notes

- **Spec coverage:** toggle per company (Task 1), local tables as sole read source (Task 2, 4, 5), `ICatalogSyncProvider`/`SapCatalogSyncProvider` (Task 3), manual-only sync — no background job added anywhere in this plan (matches spec's explicit exclusion), new `Configuracion/Integracion` page with toggle + sync + manual CRUD (Task 6), `CentrosCostoUsuario`'s own live-SAP admin picker explicitly left untouched (out of scope per spec — that page assigns cost centers to users, doesn't read the catalog Rendiciones itself validates gastos against).
- **Placeholder scan:** none found — every step has literal code or a literal command.
- **Type consistency:** `CatalogSyncResult(Created, Updated, Deactivated, Warnings)` used identically in Task 3 (definition + provider) and Task 6 (page model consumption). `CostCenter`/`GlAccount` shape from Task 2 used identically in Task 3, 4, 5, 6. `ICatalogSyncProvider` method names (`SyncCostCentersAsync`/`SyncGlAccountsAsync`) match between Task 3's interface and Task 6's page model calls.
